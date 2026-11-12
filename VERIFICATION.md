# Code-first verification — 2026-11-12

| Check | Result |
|---|---|
| Build | Passed; zero warnings/errors |
| Initial migration | Generated from the C# model and successfully applied to `SourcingTenderManagementCodeFirst` |
| Migration/model alignment | No pending model changes |
| SQL schema | 28 application tables plus EF migration history; 50 foreign keys and 34 check constraints |
| ID generation | All 21 business primary keys configured as SQL Server identity columns; request/tender/bid fixtures inserted successfully |
| Identity self-test | Password validation, correct/incorrect password checks, shared and individual committee roles; temporary user rolled back |
| Constraint checks | Passed; quantities, tender/invitation consistency, committee membership/duplicate approval, manager decisions, manager summary, and final report status; fixtures rolled back |
| HTTP checks | Passed across 38 role destinations, seven login destinations, all seven merged sidebars, account editing, supplier approval/correction, supplier History, notification privacy, and chosen-offer ownership |
| Old/new separation | Different project folders, database names, ports, authentication cookies, and migration ownership |
| Demo accounts | 13 accounts and their hashes/roles copied locally; original database was not modified |

Only the template and implemented account/read features were verified. The business workflows and disabled template buttons remain incomplete. A physical mobile device and production deployment were not tested.
