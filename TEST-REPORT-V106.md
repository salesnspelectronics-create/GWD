# گزارش GWD-106 — IIS Failure & Recovery Hardening

## مبنا

- مخزن: salesnspelectronics-create/GWD؛ فقط main.
- HEAD اولیه: 3a7289c174c9dc0a11318e0b679960dae7912561؛ نسخه 105.0.0 → 106.0.0.
- هیچ Deploy، Stop/Start واقعی IIS، تغییر سرویس Windows، Binding عملیاتی، دیتابیس، Migration یا NSPOffice انجام نشده است.
- Commit پیشنهادی: «ایمن‌سازی استقرار IIS و تفکیک نتیجه بازیابی».
- SHA و تأیید مستقل Push در گزارش تحویل گفتگو؛ این سند پیش از Commit تهیه شده است.

## یافته‌های اثبات‌شده از سورس مبنا

1. EnvironmentService.CheckIisAsync فقط نصب appcmd/Feature را بررسی می‌کرد. Wizard مرحله IIS را با نصب‌بودن تأیید می‌کرد؛ وضعیت عملیاتی Site/Pool شرط نبود. پس PASS مرحله قبلی اثبات سلامت سایت نبود.
2. StopSiteForDeploymentAsync نتیجه RunAsync فرمان‌های Stop را بررسی نمی‌کرد و ActivateStagingAsync پس از بازگشت آن IIS_STOP=PASS ثبت می‌کرد.
3. WaitForAppPoolStoppedAsync شرط `ExitCode != 0 || Output.Contains("Stopped")` داشت؛ Query ناموفق نیز PASS می‌شد. Timeout هم با پیام ادامه، FILE_DEPLOY را مجاز می‌کرد.
4. ConfigureIisAsync سایت موجود و حتی سایت متعارض روی پورت را حذف و دوباره ایجاد می‌کرد. Single Folder Recovery همان تابع را فراخوانی می‌کرد. Startها نیز unchecked بودند.
5. catch ویزارد هنگام publish=null بدون توجه به محل شکست RestartAppAsync را اجرا می‌کرد؛ حتی شکست Preflight می‌توانست به فرمان IIS منجر شود.
6. Snapshot اولیه مستقل Site/Pool در تصمیم Preflight ذخیره نمی‌شد. هیچ شاهدی وجود ندارد که علت اصلی 0x80010006 در NSPOffice یا خود GWD باشد. خرابی کانال مدیریت IIS/COM و مشکلات WAS/W3SVC فرضیه‌اند، نیازمند Event Log میزبان.

## رفتار جدید

- Preflight مستقل و fail-closed، قبل Build در Wizard/CLI و دوباره پیش از Backup/Activation. Path و Pool انتخاب‌شده با IIS بررسی می‌شوند. Stopped فقط برای هدف شناخته‌شده و بدون تلاش unresolved قابل قبول است؛ Start پس از استقرار همچنان مشروط به Health است.
- کنترل دستور appcmd با مهلت ۶۰ثانیه، ثبت Exit/HRESULT/stdout/stderr/مدت؛ فرمان ناموفق retry نمی‌شود. فقط پس از فرمان موفق، حداکثر ۱۰ مشاهده با فاصله ۵۰۰ms برای Starting/Stopping وجود دارد؛ Unknown فوراً شکست است.
- STOP Site و Pool مستقل؛ دو بار کنترل توقف قبل از آماده‌سازی و بعد از کپی staging، پیش از Swap. FILE_DEPLOY پس از شکست Stop اجرا نمی‌شود.
- Configure in-place؛ بدون delete/add site، تغییر Binding و تغییر Runtime/Identity/Pool موجود. Pool واقعی باید با Profile یکی باشد.
- Journal با Attempt ID، نسخه در Observations، مراحل PENDING/RUNNING/PASS/FAILED/SKIPPED، زمان‌ها، مشاهدات و نتیجه مستقل Recovery؛ نگارش atomic. Dispose مراحل جاری را FAILED و مراحل شروع‌نشده را SKIPPED می‌کند.
- قفل FileShare.None در ProgramData روی سه کلید Site/Pool/Path، مشترک Wizard/CLI و بین پردازه‌ها. markerهای unresolved پیش از Stop نوشته می‌شوند. موفقیت Health استقرار یا بازیابی آنها را رفع می‌کند؛ Crash یا Stop ناقص marker را نگه می‌دارد. فایل lock باقی‌مانده به‌تنهایی مانع نیست؛ handle قفل است.
- اگر فایل‌ها عوض نشده‌اند، Rollback فایل انجام نمی‌شود؛ Stop ناقص FILES_UNCHANGED / IIS_UNVERIFIED گزارش می‌شود. اگر Stop امن Recovery اثبات نشود، FILES_NOT_RESTORED / IIS_UNHEALTHY و حفظ نسخه‌ها. پس از Restore موفق، قبل از Start وضعیت FILES_RESTORED / IIS_UNHEALTHY ثبت و تنها پس از Health به IIS_HEALTHY تبدیل می‌شود.
- Diagnostics شامل مشاهده مستقل خواندنی Site/Pool/Path و Binding در خروجی list site، خطاهای System/WAS/W3SVC، هشدار صریح عدم دسترسی، Journal و Current Log است. Redaction موجود قبل از UI/Journal/ZIP استفاده می‌شود؛ web.config و داده حساس جدیدی جمع‌آوری نشده است.

## آزمون‌ها

| سناریو | نتیجه | حد اعتبار |
|---|---|---|
| Site Started | PASS | Fake هسته واقعی IisSafety؛ مستقل از Pool |
| Site Stopped | PASS | سیاست صریح پذیرش هدف موجود، Stop idempotent |
| Site Unknown | PASS | جلوگیری از Preflight/Transition؛ Starting/Stopping اولیه هم رد می‌شوند |
| appcmd غیرصفر | PASS | Exception و عدم retry |
| HRESULT 0x80010006 | PASS | Exit صفر و غیرصفر، تشخیص management و ثبت شواهد |
| Pool Started / Site Unknown | PASS | Pool سالم اجازه سلامت کاذب نمی‌دهد |
| Stop ناموفق | PASS هسته + کنترل استاتیک | انتشار شکست، عدم retry، ترتیب STOP پیش از FILE_DEPLOY؛ End-to-End فایل NOT_RUN |
| Start ناموفق پس از انتقال | PASS هسته + کنترل استاتیک | Exception Start و اتصال catch به Rollback؛ شبیه‌سازی کامل Swap/Recovery سرویس NOT_RUN |
| Restore موفق / IIS ناموفق | PASS مدل Journal + کنترل استاتیک | رشته مستقل ذخیره و ترتیب ثبت پیش از Start؛ Restore واقعی End-to-End NOT_RUN |
| Event Log غیرقابل‌خواندن | PASS | Fake جمع‌آوری‌کننده واقعی؛ سایر مشاهدات باقی می‌مانند |
| اجرای مجدد | PASS | unresolved حتی با تغییر نام سایت و Pool مشترک مانع؛ Resolve مجوز تلاش بعدی |
| دو Deploy هم‌زمان | PASS | دو Lease روی Site مشترک در یک پردازه رد می‌شود؛ رقابت دو پردازه Windows NOT_RUN |
| IisSafety.Tests | PASS | ۳۲ بررسی اجرایی .NET 9/Linux؛ فرمان Fake و مسیرهای موقت آزمون |
| verify-iis-safety.cjs | PASS | ۱۴ کنترل استاتیک اتصال Wizard/CLI/Service؛ جایگزین Integration runtime نیست |
| CodeQuality.Tests | PASS | ۳۵ بررسی موجود |
| ReleaseVersionRegression | PASS | ۱۱ بررسی موجود |
| Solution Build | PASS | Linux cross-target Windows، SDK 9.0.203، صفر Warning/Error پس از اصلاح |
| CodeQuality.WindowsTests Build | PASS | کامپایل Windows-target از Linux؛ اجرای واقعی NOT_RUN |
| git diff --check | PASS | Patch قالب صحیح |
| Windows/IIS/WinForms runtime | NOT_RUN | Windows در میزبان حاضر وجود ندارد؛ Deploy عملیاتی نیز ممنوع است |

دستورات بازتولید:

```text
dotnet run --project tests/IisSafety.Tests/IisSafety.Tests.csproj
node tests/verify-iis-safety.cjs
dotnet run --project tests/CodeQuality.Tests/CodeQuality.Tests.csproj
dotnet run --project tests/ReleaseVersionRegression/ReleaseVersionRegression.csproj
dotnet build DeploymentManager.sln -p:EnableWindowsTargeting=true -m:1
```

Baseline دست‌نخورده همین HEAD در مسیر موقت مستقل با SDK یکسان ساخته شد: WFO1000 در Ipv4AddressControl.Text و CS8604 در PathsEqual بازتولید شدند. DefaultValue واقعی `...` برای Getter چهار بخش خالی IPv4 تعیین شد و null پیش از PathsEqual رد می‌شود؛ هیچ suppression انجام نشده است.

## فایل‌های تغییرکرده

- Directory.Build.props؛ deployment-manager.version.json: نسخه هماهنگ.
- src/DeploymentManager/Services/CommandRunner.cs؛ CommandResult.cs: استخراج همان DTO برای اشتراک در آزمون بدون اجرای Process.
- src/DeploymentManager/Services/IisSafety.cs: Parser، Checked Runner و Transition.
- src/DeploymentManager/Services/IisDeploymentAttempt.cs: قفل، Journal، مراحل و unresolved.
- src/DeploymentManager/Services/IisDiagnostics.cs: Diagnostics خواندنی و تحمل شکست Event Log.
- src/DeploymentManager/Services/DeploymentService.cs: Preflight، Stop، Configure، Health و Recovery.
- src/DeploymentManager/Services/DiagnosticBundleService.cs: Export Journal.
- src/DeploymentManager/WizardForm.cs: اتصال Preflight/Lease/مراحل، حذف Restart کور و محدودیت Remote.
- src/DeploymentManager.Cli/Program.cs: Validation مستقل، Lease مشترک، Recovery برای Start/Configure exception؛ جلوگیری از حذف فایل اصلی در شکست اولیه CLI Rollback.
- src/DeploymentManager/Ipv4AddressControl.cs: متادیتای DefaultValue.
- tests/IisSafety.Tests/IisSafety.Tests.csproj؛ Program.cs: آزمون‌های Fake.
- tests/verify-iis-safety.cjs: کنترل اتصال سورس.
- CHANGELOG-V106.md؛ TEST-REPORT-V106.md؛ Project-Evolution-V106.json: سابقه و شواهد.

## ریسک‌ها و محدودیت‌ها

- Runtime واقعی Windows، رفتار Siteهای واقعی و Health کامل پس از Restore اثبات نشده است؛ Build جایگزین آنها نیست.
- ساخت سایت جدید/Pool مفقود و Deploy از مسیر Remote Wizard عمداً مسدودند. انتقال Pool، تغییر Binding و Bootstrap جدید باید توسط مدیر IIS و خارج از Recovery خودکار انجام شوند.
- مسیرهای junction/symlink مختلف canonicalize نمی‌شوند. قفل بین پردازه‌ها بر FileShare.None سیستم‌عامل متکی است؛ رقابت واقعی Windows هنوز تست نشده است.
- توقف ناقص، Crash یا Recovery نامطمئن نیاز به بازبینی دستی markerهای گزارش‌شده دارد؛ پاک‌کردن خودکار آنها ممنوع است. V105 marker جدید نداشته؛ خرابی قبلی آن فقط با وضعیت واقعی Preflight و .deployment-incomplete قابل شناسایی است.
- هدفی که در ابتدا Stopped بوده نیز پس از موفقیت Deploy طبق سیاست ارتقا Started می‌شود. حالت Unknown هرگز خودکار تعمیر یا Start نمی‌شود.
- Journal/فرمان‌ها قبل از هر اقدام ثبت می‌شوند، اما تغییر بیرونی IIS توسط ابزار دیگری خارج از قفل GWD قابل جلوگیری نیست.
- علت Windows HRESULT همچنان نامعلوم؛ رفع آن به شواهد میزبان نیاز دارد.

## اقدامات دستی برای وضعیت فعلی

۱. Deploy را تکرار نکنید؛ Backup و Bundle قبلی را حفظ کنید. در CMD مدیر، فقط مشاهدات زیر را بگیرید؛ نام‌های واقعی Profile را جایگزین کنید:

```text
%windir%\system32\inetsrv\appcmd.exe list site /name:"<SiteName>"
%windir%\system32\inetsrv\appcmd.exe list apppool /name:"<AppPoolName>" /text:state
%windir%\system32\inetsrv\appcmd.exe list vdir /vdir.name:"<SiteName>/" /text:physicalPath
```

۲. در PowerShell خواندنی: `Get-Service WAS,W3SVC`؛ سپس Event Viewer → Windows Logs → System و رویدادهای WAS/W3SVC پیرامون زمان خطا. اختلاف Binding/Path و متن کامل HRESULT بررسی شود.

۳. اگر همین فرمان خواندنی 0x80010006 می‌دهد، ابتدا مدیر Windows/IIS باید مشکل کانال مدیریتی را براساس شواهد رفع کند؛ این گزارش مجوز reset/restart سرویس یا حذف سایت نمی‌دهد.

۴. پس از تعیین Started/Stopped معتبر، مدیر فقط Site/Pool هدف را طبق تصمیم خودش بازیابی و HTTP و نسخه Backend/Frontend را بررسی کند. تنها پس از تأیید، markerهای unresolved همان تلاش و .deployment-incomplete در صورت واقعاً حل‌شدن بررسی و دستی رفع شوند؛ سپس Deploy دوباره مجاز است.
