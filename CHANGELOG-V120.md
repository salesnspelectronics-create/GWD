# V120-R — جلوگیری از استقرار ناقص IIS راه دور

- در `RemoteDeploymentService.DeployPublishAsync` خطای حذف فایل‌های قدیمی با `-ErrorAction SilentlyContinue` پنهان می‌شد.
- حذف فایل‌های قبلی اکنون با `-ErrorAction Stop` انجام می‌شود؛ شکست حذف، اجرای PowerShell را پیش از کپی نسخه جدید متوقف می‌کند.
- این اصلاح جایگزین rollback تراکنشی نیست و برای شکست پس از حذف فایل‌ها همچنان بازیابی مستقل لازم است.
- بررسی استاتیک انجام شد؛ Build، تست و Runtime اجرا نشد.
