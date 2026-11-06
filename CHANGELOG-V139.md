# V139-R — مسدودسازی مسیر مستقیم Remote فاقد بازیابی تراکنشی

مسیر مستقیم DeployPublishAsync دیگر نمی‌تواند محدودیت UI را دور بزند و بدون قفل پایدار، بکاپ اعتبارسنجی‌شده و بازیابی تراکنشی فایل‌های سرور را حذف کند. فراخوانی پیش از ایجاد WinRM session با REMOTE_DEPLOYMENT_SAFETY_BLOCK متوقف می‌شود. این تغییر پیاده‌سازی تراکنش Remote نیست.

- OPEN: remote deployment transaction, original IIS configuration/state restoration, HTTP failure recovery and owned remote staging/backup protocol still require implementation.
- Live WinRM/IIS behavior has not been executed.

STATIC_SOURCE_REVIEW=PARTIAL; AUTOMATED_TESTS=NOT_RUN; BUILD_VALIDATION=NOT_RUN; RUNTIME_REVIEW=PENDING_USER; TESTING_OWNER=USER.
