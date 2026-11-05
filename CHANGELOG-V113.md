# V113-R — جلوگیری از برخورد مسیر پشتیبان CLI

- `CreateBackup` پیش‌تر نام پوشه را فقط از زمان با دقت ثانیه و نسخه می‌ساخت؛ دو تلاش با همان زمان و نسخه ممکن بود در یک مسیر بنویسند و `CopyTree` فایل‌ها را با `overwrite=true` بازنویسی کند.
- شناسه GUID یکتا به انتهای نام هر پشتیبان افزوده شد؛ ترتیب زمانی نام حفظ می‌شود و بازنویسی ناشی از نام تکراری حذف می‌شود.
- منطق اعتبارسنجی مسیرهای نسبی، اندازه و SHA256 فایل‌ها تغییر نکرد.

## Validation
- Static source review: COMPLETED
- AUTOMATED_TESTS=NOT_RUN — USER_TESTING_POLICY
- BUILD_VALIDATION=NOT_RUN — USER_TESTING_POLICY
- RUNTIME_REVIEW=PENDING_USER
- TESTING_OWNER=USER
