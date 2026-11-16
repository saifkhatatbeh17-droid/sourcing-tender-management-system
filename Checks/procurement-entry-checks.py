"""Run procurement entry HTTP/MVC checks against a random disposable SQL database.

Uses isolated loopback port 5096 and a temporary content root for uploaded files.
The fixture drops only its named random database and deletes its temporary files
in finally. Pass --serve to retain disposable fixtures on port 5096 until the
opaque-token /qa/stop URL printed in the console is requested. Browser requests
can use X-Scoring-Test: PREVIEW_TOKEN and X-Member: supplier-1 or supplier-2.
It never starts the production Program or uses the live database.
"""
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

repo = Path(__file__).resolve().parents[1]
work = Path(tempfile.mkdtemp(prefix="procurement-entry-checks-"))
project = work / "Checks.csproj"
project.write_text(
    '<Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
    '<ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>'
    '<ItemGroup><ProjectReference Include="' + str(repo / 'SourcingTenderManagement.CodeFirst.csproj') + '" />'
    '</ItemGroup></Project>', encoding="utf-8")
shutil.copyfile(repo / "Checks/procurement-entry-harness.cs.txt", work / "Program.cs")
print("Harness:", work, flush=True)
output = work / "out"
subprocess.run(["dotnet", "build", str(project), "-p:UseAppHost=false", "-o", str(output), "-v:q"], check=True)
subprocess.run(["dotnet", str(output / "Checks.dll"), str(repo), *sys.argv[1:]], check=True)
