# V144-R — محدودسازی زمان بررسی مالک پورت IIS

در GetIisSiteUsingPort، خروجی استاندارد و خطای appcmd اکنون هم‌زمان تخلیه می‌شوند تا پر شدن بافر stderr موجب توقف خواندن stdout نشود. نتیجه WaitForExit(4000) بررسی می‌شود؛ در صورت پایان‌نیافتن فرایند، تلاش برای توقف درخت فرایند انجام شده و مالک پورت به‌صورت نامشخص برمی‌گردد. ExitCode تنها پس از خروج موفق خوانده می‌شود. رفتار fail-closed تشخیص پورت حفظ شده است.

STATIC_SOURCE_REVIEW=PARTIAL; AUTOMATED_TESTS=NOT_RUN; BUILD_VALIDATION=NOT_RUN; RUNTIME_REVIEW=PENDING_USER; TESTING_OWNER=USER.
