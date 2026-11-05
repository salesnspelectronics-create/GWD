# V115-R — محافظت از فایل‌ها در بازیابی ناموفق CLI

- پیش از حذف پوشه مقصد در catch مربوط به Rollback، وجود پوشه اصلی جابه‌جا‌شده بررسی می‌شود.
- در صورت فقدان آن پوشه، فایل‌های بازیابی ناموفق حذف نمی‌شوند و خطا برای بررسی دستی گزارش می‌شود.
- ترتیب Stop، جابه‌جایی فایل و Start در حالت عادی تغییر نکرد.

## اعتبارسنجی

Static source review: COMPLETED. Automated tests: NOT_RUN. Build validation: NOT_RUN. Runtime review: PENDING_USER. Testing owner: USER.
