# V117-R — جلوگیری از انتخاب پشتیبان ناقص CLI

- کپی و تأیید SHA256 در پوشه موقت `.incomplete-` انجام می‌شود.
- تنها پس از موفقیت تأیید، پوشه با `Directory.Move` به نام نهایی منتشر می‌شود.
- فهرست پشتیبان‌ها و `rollback --to latest` پوشه‌های موقت را نادیده می‌گیرند.
- پوشه ناقص در صورت شکست برای بررسی دستی باقی می‌ماند.

## اعتبارسنجی

Static source review: COMPLETED. Automated tests and build: NOT_RUN — USER_TESTING_POLICY. Runtime review: PENDING_USER.
