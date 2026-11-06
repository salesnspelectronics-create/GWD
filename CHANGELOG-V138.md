# V138-R — تقویت انحصار Volume و محافظت از نشانگرهای بازیابی

قفل پایدار در ریشه Volume برای جلوگیری محافظه‌کارانه از تداخل ریشه‌های هم‌پوشان؛ بررسی مالکیت handle و marker پیش از mutation/resolution؛ بررسی markerهای قدیمی والد/فرزند. UNC، mapped، SUBST و مسیرهای 8.3 مبهم صریحاً رد می‌شوند. پروفایل‌ها تغییر نمی‌کنند. قفل کل Volume هم‌زمانی برنامه‌های مستقل همان Volume را نیز محدود می‌کند.

- Windows QueryDosDevice and filesystem leases are untested.
- UNC/mapped/SUBST deployment is deliberately blocked until reliable alias coordination exists.
- Older/non-cooperating clients and external applications are not protected.

STATIC_SOURCE_REVIEW=PARTIAL; AUTOMATED_TESTS=NOT_RUN; BUILD_VALIDATION=NOT_RUN; RUNTIME_REVIEW=PENDING_USER; TESTING_OWNER=USER.
