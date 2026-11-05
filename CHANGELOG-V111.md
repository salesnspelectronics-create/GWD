# V111-R — اصلاح Restore در فضای Build موقت

در `build-release.cmd`، مرحله `robocopy` پوشه `obj` را عمداً کپی نمی‌کند، اما تصمیم قبلی برای حذف Restore بر اساس `project.assets.json` سورس اصلی بود. در نتیجه اگر Restore Stamp و assets سورس وجود داشتند، دستور `dotnet publish --no-restore` در فضای موقت فاقد assets اجرا می‌شد و ممکن بود شکست بخورد.

اکنون وجود `project.assets.json` در خود فضای موقت بررسی می‌شود؛ فضای تازه در صورت فقدان assets قبل از Publish بازسازی وابستگی می‌شود. سایر مراحل انتشار و مسیرهای خروجی تغییر نکردند.

- Static Source Review: COMPLETED
- AUTOMATED_TESTS: NOT_RUN
- BUILD_VALIDATION: NOT_RUN
- RUNTIME_REVIEW: PENDING_USER
- TESTING_OWNER: USER
