"""Run independent non-mutating/in-memory/export and isolated rejection QA suites.
Generated executable stays outside the repository. Production database never receives mutation fixtures.
"""
from pathlib import Path
import subprocess,tempfile
repo=Path(__file__).resolve().parents[1]
work=Path(tempfile.mkdtemp(prefix='tiryaq-full-qa-'))
(work/'Qa.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><ProjectReference Include="'+str(repo/'SourcingTenderManagement.CodeFirst.csproj')+'" /></ItemGroup></Project>')
(work/'Program.cs').write_text('''using SourcingTenderManagement.Checks;
var root=args[0];var output=Path.Combine(root,"output","qa","exports");Directory.CreateDirectory(output);
int passed=0,failed=0;
async Task Suite(string name,Func<Task> run){try{await run();passed++;Console.WriteLine("QA_SUITE_PASS "+name);}catch(Exception e){failed++;Console.WriteLine("QA_SUITE_FAIL "+name+" :: "+e);}}
await Suite("department-progress",()=>{DepartmentRequestProgressChecks.Run();return Task.CompletedTask;});
await Suite("committee-scoring",()=>{CommitteeScoringChecks.Run();return Task.CompletedTask;});
await Suite("committee-ranking",()=>{CommitteeRankingChecks.Run();return Task.CompletedTask;});
await Suite("offer-review",OfferReviewChecks.RunAsync);
await Suite("committee-approval-order",CommitteeApprovalOrderChecks.RunAsync);
await Suite("ranked-report",RankedReportChecks.RunAsync);
await Suite("final-report",FinalReportChecks.RunAsync);
await Suite("report-formatting",ReportContentChecks.RunAsync);
await Suite("award-notifications",TopCommitteeAwardNotificationChecks.RunAsync);
await Suite("top-reporting",()=>TopCommitteeReportingChecks.RunAsync(output));
await Suite("pdf-exports",()=>ReportPdfExportChecks.RunAsync(output));
await Suite("pdf-watermarks",()=>PdfWatermarkChecks.RunAsync(output));
await Suite("rejection-workflow",()=>RejectionWorkflowChecks.RunAsync(root));
Console.WriteLine($"QA_SUMMARY {passed} suites passed; {failed} failed");Environment.ExitCode=failed==0?0:1;
''')
subprocess.run(['dotnet','build',str(work/'Qa.csproj'),'-p:UseAppHost=false','-o',str(work/'out'),'-v:q'],check=True)
subprocess.run(['dotnet',str(work/'out/Qa.dll'),str(repo)],check=True)
