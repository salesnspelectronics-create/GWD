# گزارش تأیید V105 — Build Diagnostics & Code Quality

## مبنا و دامنه

- مخزن: salesnspelectronics-create/GWD، شاخه main.
- HEAD شروع: 782150d128cb707f41a05537fcced7c7f995effb؛ نسخه اولیه 104.0.0.
- در دریافت اولیه، HEAD با مرجع هندآف یکسان بود؛ اصلاح تکراری پس از مرجع وجود نداشت.
- نسخه جدید: 105.0.0؛ نتیجه Push و SHA نهایی در گزارش تحویل و Git ثبت می‌شود.
- هیچ IIS، دیتابیس، سرویس Windows یا زمان‌بندی تغییر نکرده است.

## نتیجه آزمون‌ها

| بررسی | وضعیت | توضیح |
|---|---|---|
| آزمون‌های اجرایی CodeQuality.Tests | PASS | ۳۵ بررسی روی Linux/.NET 9؛ Parser، هشدار بدون مکان، قالب‌های TypeScript، ANSI، خروجی چندخطی، ناشناخته، حذف تکرار، هم‌زمانی stdout/stderr، JSON، Retention، Redaction و خروجی ۱۰۰هزارخطی |
| Build کامل Solution با هدف Windows از Linux | PASS | دستور زیر؛ یک هشدار قدیمی CS8604 در DeploymentService باقی است |
| کامپایل CodeQuality.WindowsTests از Linux | PASS | تنها کامپایل؛ معادل اجرای آزمون Windows نیست |
| git diff --check | PASS | کنترل قالب Patch |
| tests/verify-static.cjs | FAIL | آزمون هش تاریخی با فایل‌های تغییریافته تطابق ندارد؛ روی checkout مستقل و دست‌نخورده مبنا نیز در DeploymentProfile.cs شکست خورد؛ fixture قدیمی تغییر داده نشد |
| اجرای Build روی Windows | NOT_RUN | Windows در محیط حاضر وجود ندارد |
| اجرای CodeQuality.WindowsTests روی Windows | NOT_RUN | آزمون‌های واقعی Process، خروجی محدودشده CommandResult، Cancel/Timeout، خرابی Store، History و ZIP آماده و کامپایل شده‌اند |
| WinForms، DPI، Clipboard و SaveFileDialog | NOT_RUN | نیازمند Windows و بازبینی بصری |
| Build واقعی NSPOffice، IIS و Deployment | NOT_RUN | هیچ استقرار واقعی انجام نشد |

دستورات قابل تکرار:

```text
dotnet run --project tests/CodeQuality.Tests/CodeQuality.Tests.csproj
dotnet build DeploymentManager.sln -p:EnableWindowsTargeting=true -m:1
dotnet build tests/CodeQuality.WindowsTests/CodeQuality.WindowsTests.csproj -p:EnableWindowsTargeting=true -m:1
```

روی Windows:

```text
dotnet build DeploymentManager.sln -m:1
dotnet run --project tests/CodeQuality.WindowsTests/CodeQuality.WindowsTests.csproj
```

MSBuild چندپردازه در میزبان Linux حاضر بدون Diagnostic با کد ۱ خاتمه یافت؛ اجرای تک‌پردازه با `-m:1` موفق بود. از این نتیجه، موفقیت اجرای Windows استنباط نمی‌شود.

## قراردادهای پیاده‌سازی

- stdout و stderr واقعی، قبل از کوتاه‌شدن CommandResult، به Collector داده می‌شوند. UI و متن کوتاه‌شده منبع تحلیل نیستند.
- Stageها Build ID، Attempt ID، ابزار، آرگومان پاک‌سازی‌شده، زمان و Exit Code دارند. Skipped یعنی فرمان جداگانه مشاهده نشده؛ Restore/Build ضمنی داخل Publish ممکن است اجرا شده باشد.
- Issue از Occurrence مستقل است؛ هویت از پروژه، کد، مکان و پیام نرمال‌شده ساخته می‌شود و نام Stage در آن نیست.
- نتیجه Build مستقل از نتیجه Deployment است؛ آخرین تلاش هر نوع Restore/Build/Publish مبنای نتیجه است. شکست اولیه اصلاح‌شده با Retry، Build موفق را ناموفق نمی‌کند.
- Exceptionهای Collector از adapter عبور نمی‌کنند و Exit Code، Exception اصلی و تصمیم Deployment را تغییر نمی‌دهند.
- JSON با SchemaVersion=1، فایل موقت، Flush و جایگزینی اتمیک نوشته می‌شود. Store در `AppDataRoot/CodeQuality` است؛ ۵۰ Build قبلی به‌همراه شواهد نگهداری می‌شوند.
- شواهد پاک‌سازی‌شده هر خط Flush می‌شوند؛ snapshot JSON برای جلوگیری از هزینه زیاد، حین سیل هشدار حداکثر هر دو ثانیه نوشته می‌شود و پایان Stage/Attempt دوباره ذخیره می‌شود.
- History تغییر فرمت نداده است. Attempt ویزارد همان Attempt موجود History است. CLI برای Publish شناسه مستقل ایجاد می‌کند و History قدیمی CLI را بازنویسی نمی‌کند.
- گزارش JSON و شواهد متنی به Bundle اضافه شده‌اند؛ فایل‌های شواهد بزرگ در ZIP به‌صورت خطی خوانده می‌شوند.
- NuGet Audit جدید یا ابزار شبکه‌ای جدیدی اجرا نمی‌شود؛ خروجی همان فرمان‌های موجود تحلیل می‌شود.

## محدودیت‌ها و ریسک‌های باقی‌مانده

- آزمون Windows و بازبینی بصری باید پیش از انتشار عملیاتی تکمیل شوند.
- در قطع ناگهانی پردازه، snapshot آخر ممکن است تا دو ثانیه از آخرین هشدار عقب باشد؛ شواهد متنی Flush‌شده باقی می‌مانند. بازسازی خودکار snapshot از این شواهد در MVP وجود ندارد.
- CommitSha می‌تواند null باشد؛ Git الزامی نشده است. EnvironmentFingerprint از محیط میزبان، Runtime، معماری و سیاست Offline ساخته می‌شود و جایگزین فهرست کامل نسخه ابزارها نیست.
- طبقه‌بندی و راهکارها قاعده‌محور هستند؛ این MVP تحلیل معنایی سورس یا اسکن امنیتی جدید نیست.
- حفظ تمام Occurrenceها و شواهد کامل، مصرف دیسک/حافظه متناسب با تعداد هشدارها دارد. Retention بر تعداد Build است و سقف حجمی ندارد.
- هشدار قدیمی `DeploymentService.cs:2023 / CS8604` باقی است و سرکوب نشده است.
- آزمون هش تاریخی مخزن نیازمند نگهداری مستقل fixtureهاست؛ شکست مبنا در checkout مستقل تأیید شد.
