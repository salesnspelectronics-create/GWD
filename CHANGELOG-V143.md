# V143-R — مسدودسازی اجرای محلی پروفایل Remote و حذف کمکی فاقد مالکیت

بررسی caller نشان داد CLI پروفایل Remote را بدون کنترل TargetMode به موتور محلی می‌داد. BeginIisAttempt اکنون حالت غیرمحلی/ناشناخته و TargetHost غیرمحلی را پیش از عملیات رد می‌کند. دو helper حذف Site/Files و AppPool، فاقد lease مشترک و بکاپ مالکیت‌دار بودند؛ اکنون پیش از STOP یا DELETE با IIS_DESTRUCTIVE_CLEANUP_SAFETY_BLOCK مسدود می‌شوند. قابلیت حذف کمکی تا پیاده‌سازی مالکیت و بازیابی مناسب در دسترس نیست.

- Remote transactional deployment remains OPEN; blocking unsafe calls is not an implementation of remote recovery.
- Auxiliary Site/Files/AppPool deletion is intentionally unavailable pending shared ownership and recoverable IIS configuration/backup prerequisites.
- User runtime must verify remote profiles never mutate local IIS and cleanup helpers reject before STOP/DELETE.

STATIC_SOURCE_REVIEW=PARTIAL; AUTOMATED_TESTS=NOT_RUN; BUILD_VALIDATION=NOT_RUN; RUNTIME_REVIEW=PENDING_USER; TESTING_OWNER=USER.
