namespace Commerce.Infrastructure;

/// <summary>Russian, Urdu and Arabic catalog text for the seed data. Real catalogs load translations, not code.</summary>
internal static class CommerceSeedTranslations
{
    public static readonly Dictionary<string, (string Ru, string Ur, string Ar)> Categories = new()
    {
        ["electronics"] = ("Электроника", "الیکٹرانکس", "إلكترونيات"),
        ["home-kitchen"] = ("Дом и кухня", "گھر اور باورچی خانہ", "المنزل والمطبخ"),
        ["books"] = ("Книги", "کتابیں", "كتب"),
    };

    public static readonly Dictionary<string, (string Ru, string Ur, string Ar)> Products = new()
    {
        ["Noise-Cancelling Headphones"] = ("Наушники с шумоподавлением", "شور ختم کرنے والے ہیڈفونز", "سماعات عازلة للضوضاء"),
        ["Mechanical Keyboard"] = ("Механическая клавиатура", "مکینیکل کی بورڈ", "لوحة مفاتيح ميكانيكية"),
        ["Portable Speaker"] = ("Портативная колонка", "پورٹیبل اسپیکر", "مكبر صوت محمول"),
        ["Smart Reading Lamp"] = ("Умная лампа для чтения", "اسمارٹ ریڈنگ لیمپ", "مصباح قراءة ذكي"),
        ["Pour-Over Coffee Set"] = ("Набор для пуровера", "پور اوور کافی سیٹ", "طقم قهوة بالتقطير اليدوي"),
        ["Cast Iron Skillet"] = ("Чугунная сковорода", "کاسٹ آئرن فرائی پین", "مقلاة من الحديد الزهر"),
        ["Platform Engineering Handbook"] = ("Справочник по платформенной инженерии", "پلیٹ فارم انجینئرنگ ہینڈ بک", "دليل هندسة المنصات"),
        ["Distributed Systems Field Guide"] = ("Практическое руководство по распределённым системам", "تقسیم شدہ نظاموں کی عملی رہنما", "الدليل الميداني للأنظمة الموزعة"),
        ["Everyday Backpack"] = ("Рюкзак на каждый день", "روزمرہ کا بیگ", "حقيبة ظهر يومية"),
        ["USB-C Travel Hub"] = ("Дорожный хаб USB-C", "یو ایس بی سی ٹریول ہب", "موزع USB-C للسفر"),
        ["Linen Sheet Set"] = ("Комплект льняного белья", "لینن چادروں کا سیٹ", "طقم ملاءات من الكتان"),
        ["Digital Kitchen Scale"] = ("Электронные кухонные весы", "ڈیجیٹل کچن اسکیل", "ميزان مطبخ رقمي"),
        ["Ergonomic Mouse"] = ("Эргономичная мышь", "ایرگونومک ماؤس", "فأرة مريحة"),
        ["Desk Organizer"] = ("Органайзер для стола", "ڈیسک آرگنائزر", "منظم مكتب"),
        ["Wireless Charging Stand"] = ("Подставка для беспроводной зарядки", "وائرلیس چارجنگ اسٹینڈ", "حامل شحن لاسلكي"),
        ["Insulated Water Bottle"] = ("Термобутылка для воды", "انسولیٹڈ پانی کی بوتل", "زجاجة ماء معزولة"),
        ["Compact Air Purifier"] = ("Компактный очиститель воздуха", "کمپیکٹ ایئر پیوریفائر", "منقي هواء صغير"),
        ["E-Reader Cover"] = ("Обложка для электронной книги", "ای ریڈر کور", "غطاء قارئ إلكتروني"),
        ["Studio Microphone"] = ("Студийный микрофон", "اسٹوڈیو مائیکروفون", "ميكروفون استوديو"),
        ["Adjustable Laptop Stand"] = ("Регулируемая подставка для ноутбука", "ایڈجسٹ ایبل لیپ ٹاپ اسٹینڈ", "حامل حاسوب محمول قابل للتعديل"),
        ["French Press"] = ("Френч-пресс", "فرنچ پریس", "مكبس قهوة فرنسي"),
        ["Cookbook for Weeknights"] = ("Кулинарная книга для будних вечеров", "ہفتے کی شاموں کے لیے کھانوں کی کتاب", "كتاب طبخ لأمسيات أيام الأسبوع"),
        ["Cable Management Kit"] = ("Набор для организации кабелей", "کیبل مینجمنٹ کٹ", "طقم تنظيم الكابلات"),
        ["Webcam Light"] = ("Подсветка для веб-камеры", "ویب کیم لائٹ", "إضاءة لكاميرا الويب"),
    };

    /// <summary>The seed descriptions are one generic sentence, so their translations are too.</summary>
    public static (string Ru, string Ur, string Ar) Description((string Ru, string Ur, string Ar) title) =>
        ($"{title.Ru}: надёжная вещь на каждый день.", $"{title.Ur}، روزمرہ استعمال کے لیے قابلِ اعتماد۔", $"{title.Ar}: خيار موثوق للاستخدام اليومي.");
}
