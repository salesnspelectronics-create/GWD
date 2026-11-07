# V148-R — اصلاح تازگی SQL و ایمنی استقرار

- تأیید SQL اکنون زمان UTC و زمان یکنواخت ثبت می‌کند؛ maxAge فراخواننده اعمال می‌شود و تغییر تنظیمات/شکست اتصال/ابطال هم‌زمان نمی‌تواند تأیید قدیمی را معتبر نگه دارد.
- قفل نشست SQL در شکست خواندن تنظیمات آزاد می‌شود؛ بازیابی حالت تک‌کاربره پیش از شروع سرویس مسلح می‌شود.
- Pool مشترک پیش از STOP رد می‌شود. مرجع بکاپ تا ثبت نهایی باقی می‌ماند و نشانگر موفقیت GUI/CLI بعد از Resolve نوشته می‌شود.
- دورزدن CA/CN/Revocation در هر سه مسیر WinRM حذف شد. SSL به گواهی معتبر نیاز دارد؛ گزینه HTTP/SSL تغییر نکرد.
- آمادگی Remote به Runtime نسخه انتخاب‌شده و ANCM وابسته است؛ IIS پیش از Hosting Bundle آماده و نتیجه نهایی دوباره بررسی می‌شود. Copy-Item از LiteralPath استفاده می‌کند.
- CLI پیش از COMMIT هویت نسخه زنده API/frontend را بررسی می‌کند و از SecretRedactor مشترک بهره می‌برد.
- دانلود نصب‌کننده تا تکمیل در فایل موقت اختصاصی باقی می‌ماند؛ ذخیره JSON هم از فایل موقت مشترک استفاده نمی‌کند.
- REMOTE_DEPLOYMENT_SAFETY_BLOCK حفظ شد. Encrypt=False و TrustServerCertificate=True به علت نبود مهاجرت سازگار گواهی تغییر نکرد.
- بازبینی ایستای محدوده‌ها و diff انجام شد؛ پوشش کل مخزن هنوز PARTIAL است. گزارش و checkpoint مسیرها، محدوده‌ها و Blobها را ثبت می‌کنند.
- AUTOMATED_TESTS=NOT_RUN — USER_TESTING_POLICY؛ BUILD_VALIDATION=NOT_RUN — USER_TESTING_POLICY؛ RUNTIME_REVIEW=PENDING_USER؛ TESTING_OWNER=USER.
