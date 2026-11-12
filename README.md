# Sourcing & Tender Management System

منصة لإدارة طلبات الشراء والمناقصات وعروض الموردين والتقييم والترسية. المستودع يبدأ بتطبيق ASP.NET Core MVC قابل للتشغيل على .NET 10، وتُضاف وظائف المشروع عبر مهام الـSprint.

## التشغيل لأول مرة

تحتاج Git و[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) بإصدار `10.0.403` أو إصدار أحدث من سلسلة `10.0` يتوافق مع `global.json`. اقبل دعوة المستودع بحساب GitHub قبل النسخ.

```bash
git clone https://github.com/saifkhatatbeh17-droid/sourcing-tender-management-system.git
cd sourcing-tender-management-system
dotnet --version
dotnet restore SourcingTender.sln
dotnet build SourcingTender.sln --no-restore
dotnet run --project src/SourcingTender.Web --launch-profile http
```

افتح [التطبيق محليًا](http://localhost:5129). أوقفه بـ `Ctrl+C`. على Visual Studio، افتح `SourcingTender.sln` بعد تثبيت workload **ASP.NET and web development** في إصدار يدعم .NET 10، ثم اختر `SourcingTender.Web` للتشغيل.

## طريقة المساهمة

اقرأ [دليل Git وطلبات الدمج للفريق](CONTRIBUTING.md). تطبيق MVC موجود تحت `src/SourcingTender.Web`، ونتائج الفحص التلقائي تظهر في تبويب **Actions** وعلى طلب الدمج.
