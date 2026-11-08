"""Run the approved dual-version/role matrix through KarenSTS2MCP.
Paths and installed test binaries are supplied in an ignored JSON configuration.
"""
from pathlib import Path
import argparse, ctypes, ctypes.wintypes as wt, itertools, json, os, shutil, socket, subprocess, threading, time, urllib.request
parser = argparse.ArgumentParser()
parser.add_argument('--config', type=Path, required=True)
parser.add_argument('--suite', choices=['matrix', 'catalog', 'performance', 'quality'], default='matrix')
parser.add_argument('--case', action='append', help='Run named cells; omit for the complete matrix')
args = parser.parse_args()
cfg = json.loads(args.config.read_text(encoding='utf-8-sig'))
root = Path(cfg['output'])
root.mkdir(parents=True, exist_ok=True)
summary = []

def atomic(path, data):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.with_suffix(path.suffix + '.tmp').write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')
    os.replace(path.with_suffix(path.suffix + '.tmp'), path)

def native_files(source, game):
    game.mkdir(parents=True, exist_ok=True)
    for path in source.iterdir():
        if path.is_file() and path.name != 'override.cfg' and (not (game / path.name).exists()):
            try:
                os.link(path, game / path.name)
            except OSError:
                shutil.copy2(path, game / path.name)
    data = game / 'data_sts2_windows_x86_64'
    if not data.exists():
        data.mkdir()
        for path in (source / data.name).rglob('*'):
            if not path.is_file():
                continue
            target = data / path.relative_to(source / data.name)
            target.parent.mkdir(parents=True, exist_ok=True)
            try:
                os.link(path, target)
            except OSError:
                shutil.copy2(path, target)
    (game / 'override.cfg').write_text(chr(10).join(['[display]', 'window/size/mode=1', 'window/size/no_focus=true', '']), encoding='utf-8')

def free_port():
    with socket.socket() as s:
        s.bind(('127.0.0.1', 0))
        return s.getsockname()[1]

class Session:

    def __init__(self, branch, role, case, index):
        self.branch = branch
        self.side = role
        self.case = case
        self.profile_id = 29000 + index * 10 + 1 if role == 'client' else 1
        self.game = root / 'games' / (branch + '-' + role)
        self.out = root / 'cases' / case / role
        if self.out.exists():
            history = root / 'attempt-history' / case / (role + '-' + str(time.time_ns()))
            history.parent.mkdir(parents=True, exist_ok=True)
            if not self.out.resolve().is_relative_to(root.resolve()) or not history.resolve().is_relative_to(root.resolve()):
                raise RuntimeError('Invalid evidence path')
            self.out.rename(history)
        self.out.mkdir(parents=True, exist_ok=True)
        self.port = free_port()
        self.api = f'http://127.0.0.1:{self.port}/api/v1'
        self.process = None
        self.closed = False
        self.focus = []
        self.minimized = []
        self.recording = False

    def prepare(self):
        source = Path(cfg['games'][self.branch])
        native_files(source, self.game)
        mods = self.game / 'mods'
        mods.mkdir(exist_ok=True)
        active = {'CoreMatrixIntegration', 'JunnaIntegration'}
        if args.suite in ['performance', 'quality']:
            active.add('KarenFirstUseProbe')
        if args.suite == 'quality':
            active.add('KarenQualityProbe')
        for name in {'KarenFirstUseProbe', 'KarenQualityProbe'} - active:
            stale = mods / name
            if stale.exists():
                if not stale.resolve().is_relative_to(self.game.resolve()):
                    raise RuntimeError('Unexpected fixture path')
                shutil.rmtree(stale)
        for src, name in [(Path(cfg['core']), 'ShoujoKagekiAijoCore'), (Path(cfg['karen']), 'ShoujoKagekiAijoKaren'), (Path(cfg['junna']), 'ShoujoKagekiJunna'), (source / 'mods/BaseLib', 'BaseLib')]:
            shutil.copytree(src, mods / name, dirs_exist_ok=True)
        shutil.copy2(cfg['mcp'][self.branch], mods / 'KarenSTS2MCP.dll')
        shutil.copy2(cfg['mcp_manifest'], mods / 'KarenSTS2MCP.json')
        for name, binary in [('CoreMatrixIntegration', cfg['matrix'][self.branch]), ('JunnaIntegration', cfg['junna_probe'][self.branch])] + ([('KarenFirstUseProbe', cfg['first_use'][self.branch])] if args.suite in ['performance', 'quality'] else []) + ([('KarenQualityProbe', cfg['quality'][self.branch])] if args.suite == 'quality' else []):
            target = mods / name
            target.mkdir(exist_ok=True)
            shutil.copy2(binary, target / (name + '.dll'))
            atomic(target / (name + '.json'), dict(id=name, name=name, version='1.0.0', has_dll=True, has_pck=False, affects_gameplay=True, dependencies=[dict(id='BaseLib'), dict(id='ShoujoKagekiAijoCore', min_version='1.1.0'), dict(id='ShoujoKagekiAijoKaren'), dict(id='ShoujoKagekiJunna'), dict(id='KarenSTS2MCP')]))
            package = self.out / 'fixture-packages'
            stage = package / name
            stage.mkdir(parents=True, exist_ok=True)
            shutil.copy2(target / (name + '.json'), stage / (name + '.json'))
            subprocess.run(['pwsh', '-NoProfile', '-File', str(Path(cfg['core']) / 'sdk/pack-character.ps1'), '-CorePackage', cfg['core'], '-CharacterDirectory', str(stage), '-OutputDirectory', str(package), '-ModId', name], check=True, stdout=subprocess.DEVNULL)
            for metadata in ['core.requirements', 'core.package.lock']:
                shutil.copy2(stage / metadata, target / metadata)
        atomic(mods / 'KarenSTS2MCP.conf', dict(port=self.port, fullscreen=False, muted=False, silent_test=True, skip_tutorial=True, skip_first_prompt=True, window_width=1280, window_height=720))
        user = self.out / 'user/AppData/Roaming/SlayTheSpire2'
        profile = user / 'default' / str(self.profile_id)
        profile.mkdir(parents=True, exist_ok=True)
        settings = json.loads(Path(cfg['settings'][self.branch]).read_text(encoding='utf-8-sig'))
        settings.update(mod_settings=dict(mods_enabled=True, mod_list=[]), fullscreen=False, seen_ea_disclaimer=True, skip_intro_logo=True, limit_fps_in_background=False, fps_limit=60)
        atomic(profile / 'settings.save', settings)
        template = Path(cfg['progress'][self.branch])
        modded = template.parents[2] / 'modded/profile1/saves/progress.save'
        if modded.exists():
            template = modded
        target = profile / 'modded/profile1/saves'
        target.mkdir(parents=True, exist_ok=True)
        if template.exists():
            shutil.copy2(template, target / 'progress.save')
        for name in ['prefs.save']:
            source = template.parent / name
            if source.exists():
                shutil.copy2(source, target / name)
        core = user / 'mod_configs'
        core.mkdir(exist_ok=True)
        settings = json.loads(Path(cfg['core_config']).read_text(encoding='utf-8-sig'))
        settings.update(EnableInformationCollection='False', EnableDataUpload='False', EnableErrorUpload='False', TelemetryBaseUrl='http://127.0.0.1:9', VfxLevel='Full')
        atomic(core / 'ShoujoKagekiAijoCore.cfg', settings)

    def start(self, mode=None):
        self.prepare()
        env = os.environ.copy()
        env['APPDATA'] = str(self.out / 'user/AppData/Roaming')
        env['LOCALAPPDATA'] = str(self.out / 'user/AppData/Local')
        Path(env['LOCALAPPDATA']).mkdir(parents=True, exist_ok=True)
        env['KAREN_ALL_VFX_RESULT'] = str(self.out / 'performance.json')
        startup = subprocess.STARTUPINFO()
        startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        startup.wShowWindow = 0
        command = [str(self.game / 'SlayTheSpire2.exe'), '--force-steam=off', f'--clientId={self.profile_id}', '--log-file', str(self.out / 'godot.log')]
        if mode:
            command.append('--fastmp=' + mode)
        self.process = subprocess.Popen(command, cwd=self.game, env=env, startupinfo=startup, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        atomic(self.out / 'session.json', dict(pid=self.process.pid, game=str(self.game), port=self.port))
        threading.Thread(target=self.monitor, daemon=True).start()
        self.wait(lambda s: s.get('menu_screen') in ['main', 'character_select'], 'initial menu', seconds=65)
        settings = self.request('/settings')
        if not settings.get('silent_test') or not settings.get('minimized_startup_guard') or settings.get('silent_test_error'):
            raise RuntimeError('Startup silent/minimized protection failed: ' + json.dumps(settings))
        atomic(self.out / 'startup-settings.json', settings)
        time.sleep(2)
        atomic(self.out / 'initial-ui.json', self.request('/ui/prompt'))
        self.wait(lambda s: s.get('menu_screen') in ['main', 'character_select'], 'ready menu')

    def monitor(self):
        user = ctypes.windll.user32
        user.GetForegroundWindow.restype = wt.HWND
        user.GetWindowThreadProcessId.argtypes = [wt.HWND, ctypes.POINTER(wt.DWORD)]
        user.IsIconic.argtypes = [wt.HWND]
        callback = ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
        while self.process and self.process.poll() is None and (not self.closed):
            foreground = user.GetForegroundWindow()
            pid = wt.DWORD()
            user.GetWindowThreadProcessId(foreground, ctypes.byref(pid))
            if pid.value == self.process.pid:
                self.focus.append(time.monotonic())

            def inspect(hwnd, param):
                owner = wt.DWORD()
                user.GetWindowThreadProcessId(hwnd, ctypes.byref(owner))
                if owner.value == self.process.pid and user.IsWindowVisible(hwnd):
                    self.minimized.append(bool(user.IsIconic(hwnd)))
                return True
            user.EnumWindows(callback(inspect), 0)
            time.sleep(0.1)

    def request(self, path, body=None):
        request = urllib.request.Request(self.api + path, data=None if body is None else json.dumps(body).encode(), headers={'Content-Type': 'application/json'})
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                return json.loads(response.read())
        except urllib.error.HTTPError as error:
            raise RuntimeError(path + ' ' + str(body) + ': ' + error.read().decode('utf-8', errors='replace')) from error

    def state(self, multi=False):
        return self.request(('/multiplayer' if multi else '/singleplayer') + '?format=json')

    def action(self, action, multi=False, **body):
        result = self.request('/multiplayer' if multi else '/singleplayer', dict(action=action, **body))
        if result.get('status') != 'ok':
            raise RuntimeError(json.dumps(dict(action=action, body=body, result=result), ensure_ascii=False))
        return result

    def command(self, command, multi=False):
        return self.action('run_command', multi=multi, command=command)

    def wait(self, predicate, label, seconds=45, multi=False):
        end = time.monotonic() + seconds
        last = None
        while time.monotonic() < end:
            if self.process.poll() is not None:
                raise RuntimeError('Game exited before ' + label)
            try:
                prompt = self.request('/ui/prompt')
                if prompt.get('active'):
                    if prompt.get('ready'):
                        option = 'advance' if prompt.get('is_tutorial') else 'no' if any((b.get('name') == 'no' and b.get('enabled') for b in prompt.get('options', []))) else None
                        if option:
                            self.request('/ui/prompt', dict(option=option, prompt_id=prompt['prompt_id']))
                    time.sleep(0.2)
                    continue
                last = self.state(multi)
                if last.get('active') and last.get('blocking'):
                    if last.get('is_tutorial') and last.get('ready'):
                        self.request('/ui/prompt', dict(option='advance', prompt_id=last['prompt_id']))
                    elif last.get('menu_screen') == 'tutorial_prompt' and last.get('ready'):
                        self.request('/ui/prompt', dict(option='no', prompt_id=last['prompt_id']))
                elif predicate(last):
                    return last
            except (OSError, ValueError, RuntimeError):
                pass
            time.sleep(0.2)
        raise RuntimeError('Timeout ' + label + ': ' + json.dumps(last, ensure_ascii=False)[:1800])

    def singleplayer(self, character):
        self.action('menu_select', option='singleplayer')
        screen = self.wait(lambda s: s.get('menu_screen') in ['singleplayer', 'character_select'], 'singleplayer entry')
        if screen.get('menu_screen') == 'singleplayer':
            self.action('menu_select', option='standard')
        self.wait(lambda s: s.get('menu_screen') == 'character_select', 'character selection')
        self.action('menu_select', option=character)
        self.action('menu_select', option='embark')
        self.wait(lambda s: s.get('player') is not None, 'run started')
        self.command('unlock all')
        self.command('fight BOWLBUGS_WEAK')
        self.wait(lambda s: s.get('battle', {}).get('is_play_phase'), 'combat')

    def shot(self, name):
        with urllib.request.urlopen(self.api + '/screenshot', timeout=30) as response:
            (self.out / (name + '.png')).write_bytes(response.read())

    def fixture(self, multi=False):
        actor = int(self.state(multi)['player']['entity_id'].split('_')[-1])
        self.command(cfg.get('fixture_command', 'matrix_fixture rules'), multi)
        end = time.monotonic() + 100
        while time.monotonic() < end:
            result = self.command('matrix_local status', multi).get('message', '')
            if result.startswith('{'):
                data = json.loads(result)
                if data.get('status') == 'done' and data.get('actor') == actor:
                    atomic(self.out / 'rules.json', data)
                    if not data.get('passed'):
                        raise RuntimeError('Rule fixture failed: ' + json.dumps(data, ensure_ascii=False))
                    return data
            time.sleep(0.25)
        raise RuntimeError('Matrix fixture timed out')

    def snapshot(self, multi=False):
        return json.loads(self.command('matrix_local snapshot', multi)['message'])

    def close(self):
        if self.closed:
            return
        self.closed = True
        if self.recording and self.process and (self.process.poll() is None):
            self.request('/recording/stop', {})
            deadline = time.monotonic() + 45
            while time.monotonic() < deadline:
                recorded = self.request('/recording')
                if recorded.get('status') in ['completed', 'failed']:
                    break
                time.sleep(0.5)
            atomic(self.out / 'recording-cleanup.json', recorded)
        atomic(self.out / 'window-protection.json', dict(focus_violations=len(self.focus), visible_window_samples=len(self.minimized), all_samples_minimized=all(self.minimized)))
        if self.process and self.process.poll() is None:
            kernel = ctypes.windll.kernel32
            kernel.OpenProcess.restype = wt.HANDLE
            handle = kernel.OpenProcess(4096, False, self.process.pid)
            size = wt.DWORD(32768)
            path = ctypes.create_unicode_buffer(size.value)
            kernel.QueryFullProcessImageNameW.argtypes = [wt.HANDLE, wt.DWORD, wt.LPWSTR, ctypes.POINTER(wt.DWORD)]
            kernel.CloseHandle.argtypes = [wt.HANDLE]
            try:
                if not handle or not kernel.QueryFullProcessImageNameW(handle, 0, path, ctypes.byref(size)) or Path(path.value).resolve() != (self.game / 'SlayTheSpire2.exe').resolve():
                    raise RuntimeError('Unexpected process image; refusing termination')
            finally:
                if handle:
                    kernel.CloseHandle(handle)
            self.process.terminate()
            self.process.wait(timeout=15)

def run_cell(branch, host_character, client_character, index):
    case = branch + '-' + host_character + ('-' + client_character if client_character else '-solo')
    sessions = []
    row = dict(case=case, passed=False)
    try:
        host = Session(branch, 'host', case, index)
        sessions.append(host)
        host.start('host_standard' if client_character else None)
        if client_character:
            client = Session(branch, 'client', case, index)
            sessions.append(client)
            client.start('join')
            for session in sessions:
                session.wait(lambda s: s.get('lobby', {}).get('player_count') == 2, 'two player lobby')
            for session, character in zip(sessions, [host_character, client_character]):
                session.action('menu_select', option=character)
            for session in sessions:
                session.action('menu_select', option='embark')
            for session in sessions:
                session.wait(lambda s: s.get('state_type') in ['map', 'event'], 'multiplayer startup', multi=True)
            if cfg.get('unlock_all'):
                for session in sessions:
                    session.command('unlock all', True)
            host.command('fight BOWLBUGS_WEAK', True)
            for session in sessions:
                session.wait(lambda s: s.get('battle', {}).get('is_play_phase'), 'multiplayer combat', multi=True)
        else:
            host.singleplayer(host_character)
        multi = bool(client_character)
        for session in sessions:
            session.shot('before')
        if multi:
            recording = host_character != client_character
            if recording:
                host.request('/recording/start', dict(output_dir=str(host.out / 'legal-play'), fps=15, max_duration_seconds=60, width=1280, height=720, audio=True))
                host.recording = True
            actions = []
            round_before = host.state(True)['battle']['round']
            for session in sessions:
                state = session.state(True)
                candidates = [c for c in state['player']['hand'] if c.get('can_play') and c.get('target_type') in ['Self', 'AnyEnemy', 'AllEnemies', 'None']]
                candidates.sort(key=lambda c: (not c['id'].endswith('DEFEND'), c.get('type') != 'Attack'))
                card = candidates[0]
                body = dict(card_index=card['index'])
                if card['target_type'] == 'AnyEnemy':
                    body['target'] = next((e['entity_id'] for e in state['battle']['enemies'] if e['hp'] > 0))
                session.action('play_card', multi=True, **body)
                actions.append(dict(player=state['player']['entity_id'], card=card['id'], action='play_card', **body))
                time.sleep(1)
            for session in sessions:
                deadline = time.monotonic() + 20
                while time.monotonic() < deadline:
                    try:
                        session.action('end_turn', multi=True)
                        break
                    except RuntimeError as error:
                        if 'being played' not in str(error) and 'actions are currently disabled' not in str(error):
                            raise
                        time.sleep(0.25)
                else:
                    raise RuntimeError('Native card action did not finish before ending turn')
            for session in sessions:
                session.wait(lambda s: s.get('battle', {}).get('round', 0) > round_before and s.get('battle', {}).get('is_play_phase'), 'next synchronized turn', multi=True)
            time.sleep(2)
            for session in sessions:
                session.shot('legal-turn')
            atomic(host.out / 'legal-actions.json', actions)
            if recording:
                host.request('/recording/stop', {})
                deadline = time.monotonic() + 60
                while time.monotonic() < deadline:
                    recorded = host.request('/recording')
                    if recorded.get('state', recorded.get('status')) in ['completed', 'failed']:
                        break
                    time.sleep(0.5)
                atomic(host.out / 'recording.json', recorded)
                host.recording = False
                if recorded.get('state', recorded.get('status')) != 'completed':
                    raise RuntimeError('Recording failed: ' + json.dumps(recorded))
                with urllib.request.urlopen(host.api + '/recording/frames?frame_count=12&columns=4&frame_width=320', timeout=60) as response:
                    (host.out / 'legal-frames.png').write_bytes(response.read())
        if multi:
            sessions[1].command('matrix_local quality Off', True)
        results = []
        for actor in sessions:
            result = actor.fixture(multi)
            results.append(result)
            if multi:
                for peer in sessions:
                    end = time.monotonic() + 90
                    while time.monotonic() < end:
                        data = json.loads(peer.command('matrix_local status', True)['message'])
                        if data.get('status') == 'done' and data.get('actor') == result['actor']:
                            break
                        time.sleep(0.2)
                    if data != result:
                        raise RuntimeError('Peers disagree on rule result')
        snapshots = [s.snapshot(multi) for s in sessions]
        for session, snapshot in zip(sessions, snapshots):
            atomic(session.out / 'snapshot.json', snapshot)
        if any((snapshot != snapshots[0] for snapshot in snapshots)):
            raise RuntimeError('Peers disagree on complete player/pile/archive snapshot')
        for session in sessions:
            deadline = time.monotonic() + 30
            while True:
                try:
                    hand = session.command('karen_check_hand', multi)
                    hand = json.loads(hand['message'])
                    atomic(session.out / 'hand-check.json', hand)
                    if (hand['modelCount'] != hand['visualCount'] or hand.get('missing') or hand.get('unexpected')
                            or hand.get('unexpectedPileIcons') or any(not item['valid'] for item in hand.get('burnOverlays', []))):
                        raise RuntimeError('Hand models and visible nodes disagree')
                    break
                except RuntimeError:
                    if time.monotonic() >= deadline:
                        raise
                    time.sleep(0.25)
            session.shot('after')
        row.update(passed=True, actors=len(sessions), checks=sum((len(r['rows']) for r in results)), peers_agree=True)
    except Exception as error:
        row['error'] = str(error)
    finally:
        for session in reversed(sessions):
            try:
                session.close()
            except Exception as error:
                row['passed'] = False
                row['cleanup_error'] = str(error)
        row['window_protection'] = [dict(focus_violations=len(s.focus), all_samples_minimized=all(s.minimized), samples=len(s.minimized)) for s in sessions]
        if any((s.focus or not all(s.minimized) for s in sessions)):
            row['passed'] = False
            row['window_protection_failed'] = True
        atomic(root / 'cases' / case / 'result.json', row)
        print(json.dumps(row, ensure_ascii=False), flush=True)
    return row

def run_special(branch, index):
    case = branch + '-' + args.suite
    session = Session(branch, 'host', case, index)
    row = dict(case=case, passed=False)
    try:
        session.start()
        session.singleplayer('JUNNA' if args.suite == 'catalog' else 'KAREN')
        time.sleep(5)
        session.shot('before')
        if args.suite == 'catalog':
            report = session.game.parent / 'card-face-results.json'
            if report.exists():
                report.unlink()
            session.command('junna_probe catalog-all')
            deadline = time.monotonic() + 500
            while time.monotonic() < deadline:
                if report.exists():
                    try:
                        data = json.loads(report.read_text(encoding='utf-8-sig'))
                        if data and data[-1].get('name') == 'complete':
                            break
                    except ValueError:
                        pass
                if session.process.poll() is not None:
                    raise RuntimeError('Game exited in catalog suite')
                time.sleep(0.5)
            else:
                raise RuntimeError('Catalog suite timed out')
            atomic(session.out / 'catalog-results.json', data)
            failures = [r for r in data if not r.get('pass')]
            row.update(checks=len(data), failures=failures, passed=not failures)
        elif args.suite == 'quality':
            budgets = json.loads(session.command('quality_probe check')['message'])
            atomic(session.out / 'budgets.json', budgets)
            if not budgets.get('passed'):
                raise RuntimeError('Quality budgets failed')
            checks = []
            for level in ['Full', 'Low', 'Off']:
                session.command('quality_probe set ' + level)
                if level == 'Full':
                    session.command('karen_common_first_use prepare shine')
                    time.sleep(2)
                before = session.snapshot()
                session.command('quality_probe prepare')
                deadline = time.monotonic() + 15
                while time.monotonic() < deadline:
                    current = session.snapshot()['players'][0]
                    promise = next(p for p in current['piles'] if p['type'] == current['promiseType'])
                    if len(promise['cards']) == 3:
                        break
                    time.sleep(.2)
                else:
                    raise RuntimeError('Generated cards did not finish moving to the promise pile')
                before = session.snapshot()
                session.command('quality_probe reset')
                session.command('quality_probe ribbon')
                session.command('quality_probe relay')
                time.sleep(0.5)
                observation = json.loads(session.command('quality_probe status')['message'])
                atomic(session.out / ('quality-' + level + '.json'), observation)
                session.shot('quality-' + level)
                if level == 'Off' and observation['visibleEffects'] != 0:
                    raise RuntimeError('Off left character VFX visible')
                after_effects = session.snapshot()
                if after_effects != before:
                    atomic(session.out / ('state-before-' + level + '.json'), before)
                    atomic(session.out / ('state-after-' + level + '.json'), after_effects)
                    raise RuntimeError('Visual tier playback changed rules, RNG or cards')
                if level == 'Off' and observation['nativeEnergyVisible'] <= 0:
                    raise RuntimeError('Off did not retain the native energy display')
                session.command('quality_probe clear ' + str(before['players'][0]['promiseType']))
                deadline = time.monotonic() + 15
                while time.monotonic() < deadline:
                    after = session.snapshot()
                    current = after['players'][0]
                    if not next(p for p in current['piles'] if p['type'] == current['promiseType'])['cards']:
                        break
                    time.sleep(.2)
                checks.append(dict(level=level, observation=observation))
            row.update(passed=True, levels=3)
        else:
            report = session.out / 'performance.json'
            if report.exists():
                report.unlink()
            session.command('karen_all_vfx_first_use run')
            deadline = time.monotonic() + 600
            while not report.exists() and time.monotonic() < deadline:
                if session.process.poll() is not None:
                    raise RuntimeError('Game exited in performance suite')
                time.sleep(0.5)
            if not report.exists():
                raise RuntimeError('Performance suite timed out')
            data = json.loads(report.read_text(encoding='utf-8-sig'))
            if data.get('status') != 'done':
                raise RuntimeError(json.dumps(data, ensure_ascii=False))
            row.update(passed=True, effects=len(data['samples']), passes=sum((len(r['passes']) for r in data['samples'])), resources=data['resources'])
        session.shot('after')
    except Exception as error:
        row['error'] = str(error)
    finally:
        session.close()
        row['focus_violations'] = len(session.focus)
        row['all_samples_minimized'] = all(session.minimized)
        if session.focus or not all(session.minimized):
            row['passed'] = False
        atomic(root / 'cases' / case / 'result.json', row)
        print(json.dumps(row, ensure_ascii=False), flush=True)
    return row
if args.suite != 'matrix':
    for index, branch in enumerate(['stable', 'beta'], 101):
        if args.case and branch + '-' + args.suite not in args.case:
            continue
        summary.append(run_special(branch, index))
        atomic(root / (args.suite + '-results.json'), summary)
    raise SystemExit(0 if summary and all((r['passed'] for r in summary)) else 1)
cells = []
for branch in ['stable', 'beta']:
    for character in ['KAREN', 'JUNNA']:
        cells.append((branch, character, None))
    for host, client in itertools.product(['KAREN', 'JUNNA'], repeat=2):
        cells.append((branch, host, client))
for index, (branch, host, client) in enumerate(cells, 1):
    case = branch + '-' + host + ('-' + client if client else '-solo')
    if args.case and case not in args.case:
        continue
    if client:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as probe:
            try:
                probe.bind(('127.0.0.1', 33771))
            except OSError as error:
                raise RuntimeError('An unrelated process owns local co-op port 33771') from error
    summary.append(run_cell(branch, host, client, index))
    atomic(root / 'matrix-results.json', summary)
raise SystemExit(0 if summary and all((row['passed'] for row in summary)) else 1)
