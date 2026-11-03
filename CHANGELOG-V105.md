# V105 — تشخیص هشدارهای Build و کیفیت کد

- افزودن کتابخانه مستقل DeploymentManager.CodeQuality بدون وابستگی خارجی جدید.
- دریافت مستقیم stdout/stderr پیش از محدودسازی CommandResult، ثبت Stage و تطبیق Attempt با History در ویزارد؛ پشتیبانی از مسیر Publish در CLI.
- Parser برای C#، NuGet، TypeScript، هشدارهای Import و Chunk در Vite و هشدارهای ناشناخته؛ نگهداری ادامه‌های چندخطی و شواهد کامل پاک‌سازی‌شده.
- تفکیک Issue از Occurrence، ذخیره JSON نسخه‌دار و اتمیک، نگهداری ۵۰ Build اخیر همراه با شواهد.
- تب Code Quality شامل شمارنده، فیلتر، جزئیات، راهکارهای قاعده‌محور، خروجی JSON/Markdown و کپی هندآف.
- افزودن گزارش‌های کیفیت و شواهد به Diagnostic Bundle؛ قرارداد History و منطق تصمیم Build/Deployment تغییر نکرده است.
- تفکیک نتیجه Build از Deployment، ثبت لغو/وقفه، و اختیاری‌ماندن Collector در صورت خطا.

## تأیید

۳۵ آزمون هسته روی Linux پاس شد. Solution و پروژه آزمون Windows با هدف net9.0-windows از Linux کامپایل شدند. اجرای واقعی Windows، WinForms، IIS و آزمون Integration ویندوز NOT_RUN است. یک هشدار CS8604 قدیمی در DeploymentService باقی است؛ سرکوب نشده است. جزئیات در TEST-REPORT-V105.md.
