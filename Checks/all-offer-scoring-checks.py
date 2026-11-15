"""Run actual MVC scoring requests in a task-created SQL database, never the user database.

Use --serve to keep a synthetic preview alive after the checks; stop it using the
token-protected /qa/stop URL on the temporary loopback host.
"""
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

repo = Path(__file__).resolve().parents[1]
work = Path(tempfile.mkdtemp(prefix="all-offer-scoring-checks-"))
project = work / "Checks.csproj"
project.write_text(
    '<Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
    '<ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>'
    '<ItemGroup><ProjectReference Include="' + str(repo / 'SourcingTenderManagement.CodeFirst.csproj') + '" />'
    '</ItemGroup></Project>', encoding="utf-8")
shutil.copyfile(repo / "Checks/all-offer-scoring-harness.cs.txt", work / "Program.cs")
print("Harness:", work, flush=True)
output = work / "out"
subprocess.run(["dotnet", "build", str(project), "-p:UseAppHost=false", "-o", str(output), "-v:q"], check=True)
subprocess.run(["dotnet", str(output / "Checks.dll"), str(repo), *sys.argv[1:]], check=True)
