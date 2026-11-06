# V141-R — گزارش فقط‌خواندنی ظرفیت و برنامه محافظه‌کارانه نگهداری

دستور backups plan --root گزارش ظرفیت و حجم artifactها را بدون ساخت AppDataService، ذخیره پروفایل یا نوشتن result-file تولید می‌کند. پیش‌فرض برنامه حداقل ۵ نسخه و ۳۰ روز است؛ همه artifactها تا اثبات مالکیت و resolution محافظت می‌شوند. EligibleForDeletion خالی است؛ هیچ API یا گزینه حذف خودکار اضافه نشده است.

- SOURCE_COMPLETE_RUNTIME_PENDING_USER: reporting-only infrastructure; deletion is intentionally unavailable.
- MinimumKeep/MinimumAgeDays are planning parameters, not deletion authorization.
- Capacity is a point-in-time observation; inaccessible/reparse trees are protected and reported incomplete.
- Resolved artifact ownership is not yet sufficient to safely nominate deletion candidates.

STATIC_SOURCE_REVIEW=PARTIAL; AUTOMATED_TESTS=NOT_RUN; BUILD_VALIDATION=NOT_RUN; RUNTIME_REVIEW=PENDING_USER; TESTING_OWNER=USER.
