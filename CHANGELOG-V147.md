# V147-R — اعتبارسنجی پاسخ آمادگی Remote

- خروجی PowerShell تنها با ExitCode صفر موفق محسوب نمی‌شود؛ JSON معتبر با فیلدهای Administrator، IIS و Hosting از نوع Boolean و Computer غیرخالی الزامی است.
- خروجی خالی، نامعتبر یا ناقص اکنون Connected=false برمی‌گرداند و Bootstrap از ادامه روی وضعیت جعلی جلوگیری می‌کند.
- سیاست مسدودبودن Remote DeployPublishAsync بدون تغییر باقی ماند.
- بازبینی صرفاً استاتیک بود؛ تست، کامپایل، Build و Runtime اجرا نشد.
