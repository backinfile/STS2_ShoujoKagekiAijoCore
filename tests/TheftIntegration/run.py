"""Run native stolen-card return checks through KarenSTS2MCP."""
import argparse
import ctypes
import ctypes.wintypes as wt
import hashlib
import json
import os
from pathlib import Path
import shutil
import socket
import subprocess
import time
import threading
import urllib.request

parser = argparse.ArgumentParser()
parser.add_argument('--game', type=Path, required=True)
parser.add_argument('--character', default='IRONCLAD')
parser.add_argument('--mod', type=Path, action='append', default=[])
parser.add_argument('--core', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--probe', type=Path, required=True)
args = parser.parse_args()
root = args.output.resolve()
root.mkdir(parents=True, exist_ok=True)
game = root / 'game'
user = root / 'user'
process = None
api = None
focus_violations = []
window_samples = []
monitor_thread = None
phase = 'prepare'


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')


def request(path, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(api + path, data=data, headers={'Content-Type': 'application/json'})
    with urllib.request.urlopen(req, timeout=15) as response:
        return json.load(response)


def action(name, **values):
    reply = request('/singleplayer', dict(action=name, **values))
    if reply.get('status') != 'ok':
        raise RuntimeError(f'{name}: {reply}')
    return reply


def wait(predicate, label, seconds=50):
    deadline = time.monotonic() + seconds
    last = None
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise RuntimeError(f'Game exited before {label}')
        try:
            prompt = request('/ui/prompt')
            if prompt.get('active') and prompt.get('ready'):
                option = 'advance' if prompt.get('is_tutorial') else 'no'
                request('/ui/prompt', dict(option=option, prompt_id=prompt['prompt_id']))
            last = request('/singleplayer?format=json')
            if not prompt.get('active') and predicate(last):
                return last
        except (OSError, ValueError):
            pass
        time.sleep(0.25)
    raise RuntimeError(f'Timeout {label}: {last}')


def command(text):
    return action('run_command', command=text)


def check(mode):
    command('core_theft ' + mode)
    deadline = time.monotonic() + 20
    while time.monotonic() < deadline:
        message = command('core_theft status').get('message', '')
        if message.startswith('{'):
            result = json.loads(message)
            write(root / f'{phase}-{mode}.json', result)
            if not result['passed']:
                raise RuntimeError(json.dumps(result, ensure_ascii=False))
            print(f'{phase} {mode}: PASS', flush=True)
            return
        time.sleep(0.2)
    raise RuntimeError(f'Timeout {mode}')


def start():
    global process, api, monitor_thread
    with socket.socket() as listener:
        listener.bind(('127.0.0.1', 0))
        port = listener.getsockname()[1]
    api = f'http://127.0.0.1:{port}/api/v1'
    write(game / 'mods/KarenSTS2MCP.conf', dict(port=port, fullscreen=False, muted=False,
          silent_test=True, skip_tutorial=True, skip_first_prompt=True, window_width=1280, window_height=720))
    env = os.environ.copy()
    env.update(APPDATA=str(user / 'AppData/Roaming'), LOCALAPPDATA=str(user / 'AppData/Local'))
    startup = subprocess.STARTUPINFO()
    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    startup.wShowWindow = 0
    process = subprocess.Popen([str(game / 'SlayTheSpire2.exe'), '--force-steam=off',
        '--rendering-method', 'gl_compatibility', '--log-file', str(root / f'{phase}.log')],
        cwd=game, env=env, startupinfo=startup, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    monitor_thread = threading.Thread(target=monitor, args=(process,), daemon=True)
    monitor_thread.start()
    write(root / 'instance.json', dict(pid=process.pid, port=port, exe=str(game / 'SlayTheSpire2.exe')))
    wait(lambda s: s.get('menu_screen') == 'main' and any(o in s.get('options', []) for o in ['singleplayer', 'continue']), 'main menu')
    settings = request('/settings')
    write(root / f'{phase}-settings.json', settings)
    if not settings.get('silent_test') or not settings.get('minimized_startup_guard') or settings.get('silent_test_error'):
        raise RuntimeError(f'Silent/minimized startup failed: {settings}')
    request('/window/minimize', {})


def monitor(child):
    native = ctypes.windll.user32
    native.GetForegroundWindow.restype = wt.HWND
    native.GetWindowThreadProcessId.argtypes = [wt.HWND, ctypes.POINTER(wt.DWORD)]
    native.IsIconic.argtypes = [wt.HWND]
    native.IsWindowVisible.argtypes = [wt.HWND]
    callback = ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    while child.poll() is None:
        foreground_pid = wt.DWORD()
        native.GetWindowThreadProcessId(native.GetForegroundWindow(), ctypes.byref(foreground_pid))
        if foreground_pid.value == child.pid:
            focus_violations.append(child.pid)
        def inspect(hwnd, _):
            owner = wt.DWORD()
            native.GetWindowThreadProcessId(hwnd, ctypes.byref(owner))
            if owner.value == child.pid and native.IsWindowVisible(hwnd):
                window_samples.append(bool(native.IsIconic(hwnd)))
            return True
        native.EnumWindows(callback(inspect), 0)
        time.sleep(0.05)


def screenshot(name):
    with urllib.request.urlopen(api + '/screenshot', timeout=15) as response:
        (root / f'{phase}-{name}.png').write_bytes(response.read())


def stop():
    if process and process.poll() is None:
        kernel = ctypes.windll.kernel32
        kernel.QueryFullProcessImageNameW.argtypes = [wt.HANDLE, wt.DWORD, wt.LPWSTR, ctypes.POINTER(wt.DWORD)]
        size = wt.DWORD(32768)
        actual = ctypes.create_unicode_buffer(size.value)
        if not kernel.QueryFullProcessImageNameW(wt.HANDLE(int(process._handle)), 0, actual, ctypes.byref(size)):
            raise RuntimeError('Cannot verify the test process image; refusing termination.')
        if Path(actual.value).resolve() != (game / 'SlayTheSpire2.exe').resolve():
            raise RuntimeError('Unexpected process image; refusing termination.')
        process.terminate()
        process.wait(timeout=10)
        if monitor_thread:
            monitor_thread.join(timeout=2)


try:
    if game.exists():
        raise RuntimeError('Use a fresh output directory; previous evidence is preserved.')
    game.mkdir()
    for source in args.game.iterdir():
        if source.is_file() and source.name != 'override.cfg':
            os.link(source, game / source.name)
    # Reuse native references; keep mods, settings and saves in the isolated directory.
    subprocess.run(['cmd', '/c', 'mklink', '/J', str(game / 'data_sts2_windows_x86_64'),
                    str((args.game / 'data_sts2_windows_x86_64').resolve())], check=True, capture_output=True)
    mods = game / 'mods'
    mods.mkdir()
    for name in ['BaseLib', 'BaseLibCompatibilityHelper']:
        source = args.game / 'mods' / name
        if source.is_dir():
            shutil.copytree(source, mods / name)
    for source in (args.game / 'mods').glob('BaseLibCompatibilityHelper.*'):
        shutil.copy2(source, mods / source.name)
    shutil.copytree(args.core, mods / 'ShoujoKagekiAijoCore')
    for extra in args.mod:
        shutil.copytree(extra, mods / extra.name)
    for name in ['KarenSTS2MCP.dll', 'KarenSTS2MCP.json']:
        shutil.copy2(args.game / 'mods' / name, mods / name)
    fixture = mods / 'CoreTheftProbe'
    fixture.mkdir()
    shutil.copy2(args.probe, fixture / 'CoreTheftProbe.dll')
    shutil.copy2(Path(__file__).with_name('CoreTheftProbe.json'), fixture)
    shutil.copy2(Path(__file__).with_name('core.requirements'), fixture)
    (game / 'override.cfg').write_text('[display]\nwindow/size/mode=1\nwindow/size/no_focus=true\n', encoding='utf-8')
    (user / 'AppData/Local').mkdir(parents=True)
    write(user / 'AppData/Roaming/SlayTheSpire2/default/1/settings.save', dict(schema_version=8,
          mod_settings=dict(mods_enabled=True, mod_list=[]), fullscreen=False, seen_ea_disclaimer=True,
          skip_intro_logo=True, language='eng', limit_fps_in_background=False, volume_master=0.5))
    write(user / 'AppData/Roaming/SlayTheSpire2/mod_configs/ShoujoKagekiAijoCore.cfg',
          dict(EnableInformationCollection='False', EnableDataUpload='False', EnableErrorUpload='False'))
    write(root / 'versions.json', dict(game=json.loads((args.game / 'release_info.json').read_text(encoding='utf-8-sig')), core=json.loads((args.core / 'core.bundle').read_text(encoding='utf-8-sig'))['releaseVersion'], character=args.character))
    phase = 'theft'
    start()
    action('menu_select', option='singleplayer')
    screen = wait(lambda s: s.get('menu_screen') in ['singleplayer', 'character_select'], 'singleplayer')
    if screen.get('menu_screen') == 'singleplayer':
        action('menu_select', option='standard')
    wait(lambda s: s.get('menu_screen') == 'character_select', 'character select')
    action('menu_select', option=args.character)
    action('menu_select', option='embark')
    wait(lambda s: s.get('player') is not None, 'run')
    command('unlock all')
    wait(lambda s: s.get('state_type') == 'map', 'initial map')
    action('choose_map_node', index=0)
    wait(lambda s: s.get('battle', {}).get('is_play_phase'), 'combat fixture')
    screenshot('before')
    check('run')
    screenshot('after')
    time.sleep(3)
    screenshot('settled')
    write(root / 'result.json', dict(passed=True))
except Exception as error:
    write(root / 'result.json', dict(passed=False, phase=phase, error=str(error)))
    raise
finally:
    stop()
    write(root / 'window-protection.json', dict(focus_violations=len(focus_violations), window_samples=len(window_samples), all_minimized=bool(window_samples) and all(window_samples)))
    if game.exists():
        write(root / 'tested-hashes.json', {str(p.relative_to(game)).replace('\\', '/'): hashlib.sha256(p.read_bytes()).hexdigest() for p in (game / 'mods').rglob('*') if p.is_file() and p.suffix in ['.dll', '.pck']})
