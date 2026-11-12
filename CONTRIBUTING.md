# دليل تنزيل المشروع ورفع التغييرات

`main` هو الفرع المشترك الذي يبدأ منه الجميع. لكل مهمة **Issue واحد، وفرع مؤقت، وطلب دمج واحد إلى `main`**. السكَرَم ماستر يدمج الطلب بعد موافقة زميل ونجاح الفحص. الرفع المباشر إلى `main` ممنوع ضمن اتفاق الفريق.

| نوع المهمة | مثال لاسم الفرع |
| --- | --- |
| ميزة | `feature/12-purchase-request` |
| إصلاح خطأ | `fix/27-bid-validation` |
| توثيق | `docs/31-team-setup` |

استخدم رقم الـIssue ووصفًا قصيرًا بالإنجليزية. إذا كانت مهمتان ستغيّران نفس الملف أو نموذج البيانات، اتفقوا على ترتيب العمل قبل بدء التعديل.

## تنزيل مجلد المشروع لأول مرة

1. اقبل دعوة المستودع من إشعارات GitHub أو البريد بالحساب الذي استلم الدعوة، وتأكد أنك تستطيع فتح صفحة المستودع.
2. ثبّت [Git](https://git-scm.com/install/windows) و[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). يبيّن `global.json` إصدار الـSDK المطلوب.
3. افتح PowerShell داخل المجلد **الأب** الذي تريد حفظ المشروع تحته، مثل مجلد Documents. نفّذ الأوامر التالية. ينشئ `git clone` مجلد المشروع تلقائيًا.

```powershell
git --version
dotnet --list-sdks
git clone https://github.com/saifkhatatbeh17-droid/sourcing-tender-management-system.git
cd sourcing-tender-management-system
git status
```

إذا ظهر طلب تسجيل الدخول، استخدم حساب GitHub الذي قبل الدعوة. إذا ظهر `Repository not found`، افتح رابط المستودع من نفس الحساب وتحقق من قبول الدعوة. إذا نزّلت ZIP سابقًا، استخدم نسخة `git clone` للعمل والرفع، وانقل إليها فقط تعديلاتك إن وجدت.

افتح `SourcingTender.sln` من المجلد الجديد في Visual Studio، أو شغّل المشروع من PowerShell كما يلي.

```powershell
dotnet restore SourcingTender.sln
dotnet run --project src/SourcingTender.Web --launch-profile http
```

افتح [الصفحة المحلية](http://localhost:5129). أوقف التطبيق بـ `Ctrl+C` عند الانتهاء.

## بدء مهمة

1. افتح الـIssue أو أنشئه، عيّن نفسك، واتفق مع الفريق على معايير القبول.
2. تأكد عبر `git status` أن تغييرات مهمتك السابقة محفوظة. حدّث `main` وابدأ فرع المهمة. رقم المهمة في المثال هو 12.

```bash
git switch main
git pull --ff-only origin main
git switch -c feature/12-purchase-request
git branch --show-current
```

تأكد أن الأمر الأخير يعرض فرع المهمة قبل أن تبدأ التعديل.

## رفع التغيير

```bash
dotnet build SourcingTender.sln
dotnet test SourcingTender.sln --no-build
git status
git branch --show-current
git diff
git add -- "path/to/changed-file"
git diff --cached
git commit -m "feat: add purchase request form"
git push -u origin HEAD
```

استبدل `path/to/changed-file` بمسار الملف الذي عدّلته، وكرّر `git add` للملفات المعنية. راجع `git diff --cached` قبل الـcommit وتأكد أن فرعك يبدأ بـ `feature/` أو `fix/` أو `docs/` قبل الرفع.

إذا طلب Git تعريف صاحب الـcommit أول مرة، اضبط الاسم وبريد GitHub على هذا المستودع، ثم أعد أمر الـcommit.

```bash
git config user.name "Your Name"
git config user.email "YOUR_GITHUB_EMAIL"
```

## فتح Pull Request

1. من GitHub اضغط **Compare & pull request**، أو افتح **Pull requests → New pull request**.
2. اختر `main` في **base** وفرع المهمة في **compare**. راجع **Files changed** للتأكد أن الطلب يحتوي تغييرات مهمتك.
3. اكتب ما تغيّر وكيف جرّبته. أضف `Closes #12` عند اكتمال المهمة، واطلب مراجعة زميل. استخدم **Draft** إذا كان العمل ما زال جاريًا.
4. إذا طُلب تعديل، نفّذه على نفس الفرع، واعمل commit ثم `git push`. يتحدّث الطلب الموجود تلقائيًا.

## مراجعة ودمج

- يراجع زميل التغييرات ويجرّب السيناريو المذكور، ثم يوافق أو يطلب تعديلات.
- انتظر نجاح **Build and smoke test** ومعالجة محادثات المراجعة. اختبر الصلاحيات وانتقالات الحالات عندما يمسها التغيير.
- السكَرَم ماستر ينفّذ **Squash and merge**. GitHub يحذف فرع المهمة البعيد تلقائيًا بعد الدمج.
- إذا احتجت تحديث فرعك، نفّذ `git fetch origin` ثم `git merge origin/main` من **فرع المهمة**. إذا ظهر تعارض، اتفق مع صاحب التعديل الآخر على النتيجة، أضف الملفات التي حللتها عبر `git add` وأكمل الدمج بـ `git commit`. أعد البناء ثم نفّذ `git push`.
- بعد الدمج ارجع إلى `main` ونفّذ `git pull --ff-only origin main` قبل بدء المهمة التالية.

المستودع الخاص يعمل حاليًا على خطة لا تفرض حماية `main`. لذلك على الفريق الالتزام باتفاق الرفع والمراجعة يدويًا. عند توفر حماية الفرع، تُضبط لطلب Pull Request وموافقة واحدة على الأقل ونجاح **Build and smoke test** وحل محادثات المراجعة، مع تعطيل الرفع القسري وحذف `main`. [توثيق حماية الفروع](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches)

## ملفات العمل المحلية

استخدم إعدادات البيئة أو ASP.NET Core User Secrets للأسرار المحلية عند إضافة التكاملات. احتفظ بكلمات المرور وسلاسل الاتصال الحقيقية وبيانات الموردين ووثائقهم خارج Git. استخدم بيانات تجريبية عند إرفاق لقطات أو أمثلة.
