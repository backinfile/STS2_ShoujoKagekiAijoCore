"""Compact read indexes derived transactionally from accepted run facts."""


_SCHEMA = """
CREATE TABLE IF NOT EXISTS stats_runs (
    run_id TEXT PRIMARY KEY,
    language TEXT NOT NULL DEFAULT 'unknown',
    core_version TEXT,
    game_target TEXT,
    card_seats TEXT NOT NULL DEFAULT '[]'
);
CREATE TABLE IF NOT EXISTS stats_characters (
    run_id TEXT NOT NULL,
    character TEXT NOT NULL,
    seats INTEGER NOT NULL,
    PRIMARY KEY (run_id, character)
) WITHOUT ROWID;
CREATE TABLE IF NOT EXISTS stats_sources (
    run_id TEXT NOT NULL,
    source_mod TEXT NOT NULL,
    PRIMARY KEY (run_id, source_mod)
) WITHOUT ROWID;
CREATE INDEX IF NOT EXISTS stats_builds ON runs(build);
CREATE INDEX IF NOT EXISTS stats_by_language ON stats_runs(language, run_id);
CREATE INDEX IF NOT EXISTS stats_by_core_version ON stats_runs(core_version, run_id);
CREATE INDEX IF NOT EXISTS stats_by_game_target ON stats_runs(game_target, run_id);
CREATE INDEX IF NOT EXISTS stats_by_character ON stats_characters(character, run_id);
CREATE INDEX IF NOT EXISTS stats_by_source ON stats_sources(source_mod, run_id);

CREATE TRIGGER IF NOT EXISTS stats_runs_populate AFTER INSERT ON stats_runs BEGIN
    UPDATE stats_runs
    SET (language, core_version, game_target, card_seats) = (
        SELECT COALESCE(json_extract(facts, '$.core_telemetry.language'), 'unknown'),
               json_extract(facts, '$.core_telemetry.core_version'),
               json_extract(facts, '$.core_telemetry.game_target'),
               (SELECT json_group_array(json_array(
                    json_extract(seat.value, '$.character'),
                    json_extract(seat.value, '$.held'),
                    json_extract(seat.value, '$.missing_picked'),
                    (SELECT json_group_array(json_array(json_extract(choice.value, '$.card'),
                                                       json_extract(choice.value, '$.picked')))
                     FROM json_each(seat.value, '$.observed') AS choice)
                )) FROM json_each(facts, '$.seats') AS seat)
        FROM runs WHERE id=NEW.run_id
    )
    WHERE run_id=NEW.run_id;
    INSERT INTO stats_characters
        SELECT NEW.run_id, json_extract(seat.value, '$.character'), COUNT(*)
        FROM runs CROSS JOIN json_each(facts, '$.seats') AS seat
        WHERE runs.id=NEW.run_id
        GROUP BY json_extract(seat.value, '$.character');
    INSERT INTO stats_sources
        SELECT DISTINCT NEW.run_id, json_extract(mod.value, '$.id')
        FROM runs CROSS JOIN json_each(facts, '$.core_telemetry.source_mods') AS mod
        WHERE runs.id=NEW.run_id;
END;

CREATE TRIGGER IF NOT EXISTS runs_stats_insert AFTER INSERT ON runs BEGIN
    INSERT INTO stats_runs(run_id) VALUES(NEW.id);
END;

CREATE TRIGGER IF NOT EXISTS runs_stats_update AFTER UPDATE OF facts ON runs BEGIN
    DELETE FROM stats_characters WHERE run_id=OLD.id;
    DELETE FROM stats_sources WHERE run_id=OLD.id;
    DELETE FROM stats_runs WHERE run_id=OLD.id;
    INSERT INTO stats_runs(run_id) VALUES(NEW.id);
END;

CREATE TRIGGER IF NOT EXISTS runs_stats_delete AFTER DELETE ON runs BEGIN
    DELETE FROM stats_characters WHERE run_id=OLD.id;
    DELETE FROM stats_sources WHERE run_id=OLD.id;
    DELETE FROM stats_runs WHERE run_id=OLD.id;
END;
"""


def initialize(db):
    # Triggers also maintain the indexes when an older release writes to the same database.
    # A single transaction covers schema creation and the first backfill.
    db.executescript("BEGIN IMMEDIATE;\n" + _SCHEMA + """
        INSERT INTO stats_runs(run_id)
        SELECT id FROM runs WHERE NOT EXISTS (SELECT 1 FROM stats_runs WHERE run_id=runs.id);
        COMMIT;
    """)
