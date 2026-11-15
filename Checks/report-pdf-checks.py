"""Integration QA against real MVC reads and PDFsharp/MigraDoc FileResults.

Playwright only clicks website buttons and captures UI screenshots. It never
prints or generates a PDF. The production implementation has no browser dependency.
SQL fixtures use one transaction, never committed, with no schema changes.
"""
from pathlib import Path
import os, shutil, subprocess, tempfile, time, urllib.request

repo=Path(__file__).resolve().parents[1]
work=Path(tempfile.mkdtemp(prefix='tender-migradoc-qa-'))
output=work/'artifacts';output.mkdir()
(work/'Harness.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><ProjectReference Include="'''+str(repo/'SourcingTenderManagement.CodeFirst.csproj')+'''" /></ItemGroup></Project>''',encoding='utf-8')
shutil.copyfile(repo/'Checks/report-pdf-harness.cs.txt',work/'Program.cs')
env=dict(os.environ)
env.setdefault('CODEX_NODE_MODULES',str(Path.home()/'.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules'))
log=(work/'host.log').open('w',encoding='utf-8')
host=subprocess.Popen(['dotnet','run','--project',str(work/'Harness.csproj'),'--no-launch-profile','--',str(repo),str(output)],cwd=repo,env=env,stdout=log,stderr=subprocess.STDOUT,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
print('QA artifacts:',output,flush=True)
try:
    for _ in range(150):
        if host.poll() is not None:raise RuntimeError((work/'host.log').read_text(encoding='utf-8')[-8000:])
        try:
            with urllib.request.urlopen('http://127.0.0.1:5088/qa/health',timeout=1) as response:
                if response.status==200:break
        except OSError:time.sleep(1)
    else:raise RuntimeError('QA host did not start.')
    subprocess.run(['node',str(repo/'Checks/report-pdf-browser.cjs'),str(output)],cwd=repo,env=env,check=True)
    subprocess.run([os.sys.executable,str(repo/'Checks/report-pdf-verify.py'),str(output)],cwd=repo,env=env,check=True)
finally:
    try:
        urllib.request.urlopen('http://127.0.0.1:5088/qa/stop',timeout=3).close();host.wait(timeout=20)
    except Exception:host.kill();host.wait()
    fixture_marker=output/'uploaded-fixture-path.txt'
    if fixture_marker.exists():
        fixture=Path(fixture_marker.read_text(encoding='utf-8')).resolve()
        expected=(repo/'App_Data/private-files/offers').resolve()
        if fixture.parent==expected and fixture.name.startswith('pdf-qa-') and fixture.suffix=='.pdf':
            fixture.unlink(missing_ok=True)
    log.close()
    print('QA host stopped; its SQL transaction was never committed.',flush=True)
