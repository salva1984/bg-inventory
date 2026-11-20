namespace BgInventario.Infrastructure.Persistence;

/// <summary>A kind of product the generator can create, with the USD price range that makes sense for it.</summary>
internal sealed record SampleProductType(string Name, string CodePrefix, string Description, decimal MinPrice, decimal MaxPrice);

/// <summary>A fictional distributor; its contact email is built on <see cref="EmailDomain"/>.</summary>
internal sealed record SampleSupplier(string Name, string EmailDomain);

/// <summary>
/// Hand-curated vocabulary for the generated sample data. Bogus picks from these lists instead of using its
/// own commerce generator, whose names ("Ergonómico Granito Silla") do not look like a tech store.
/// Code prefixes avoid MON, SND, LAP, TEC and MOU, already used by the fixed products of the challenge.
/// </summary>
internal static class SampleCatalog
{
    public static readonly SampleProductType[] ProductTypes =
    [
        new("Monitor", "MNT", "Monitor LED para oficina y hogar", 120, 600),
        new("Laptop", "NBK", "Computadora portátil para trabajo, estudio y uso diario", 350, 1400),
        new("Impresora", "IMP", "Impresora de inyección o láser para documentos de oficina", 90, 450),
        new("Router", "RTR", "Router inalámbrico para conexión doméstica o de pequeña empresa", 35, 300),
        new("Audífonos", "AUD", "Audífonos con o sin cable para música y llamadas", 15, 350),
        new("Parlante", "PAR", "Parlante portátil o de escritorio con sonido estéreo", 20, 400),
        new("Teclado", "TCL", "Teclado con cable o inalámbrico para computadora", 12, 180),
        new("Mouse", "RAT", "Mouse óptico o inalámbrico para uso diario", 8, 120),
        new("Disco SSD", "SSD", "Unidad de estado sólido para acelerar el arranque y los programas", 25, 250),
        new("Disco duro externo", "HDD", "Disco duro portátil para respaldos y almacenamiento adicional", 45, 180),
        new("Cámara web", "CWB", "Cámara web HD para videollamadas y reuniones en línea", 25, 150),
        new("Tablet", "TAB", "Tableta para navegación, estudio y entretenimiento", 90, 900),
        new("Cámara de seguridad", "CAM", "Cámara IP para vigilancia de hogares y negocios", 30, 200),
        new("Teléfono inteligente", "CEL", "Teléfono móvil con pantalla táctil y conexión 4G o 5G", 120, 1200),
        new("Memoria USB", "USB", "Memoria flash USB para transferir y guardar archivos", 6, 60),
        new("Tarjeta de memoria", "MSD", "Tarjeta microSD para cámaras, celulares y consolas", 8, 90),
        new("Cable HDMI", "HDM", "Cable HDMI para conectar televisores, monitores y proyectores", 3, 40),
        new("Cargador portátil", "PWB", "Batería externa USB para recargar celulares y tablets", 15, 120),
        new("Proyector", "PRO", "Proyector para presentaciones y salas de clase", 150, 900),
        new("Escáner", "ESC", "Escáner de documentos para digitalizar archivos en PDF", 80, 400),
        new("Switch de red", "SWR", "Switch de red para conectar varios equipos por cable Ethernet", 20, 250),
        new("Access point", "APT", "Punto de acceso Wi-Fi para ampliar la cobertura inalámbrica", 45, 300),
        new("Tarjeta gráfica", "GPU", "Tarjeta de video para juegos, diseño y edición", 150, 1200),
        new("Procesador", "CPU", "Procesador de escritorio para armar o actualizar equipos", 90, 600),
        new("Memoria RAM", "RAM", "Módulo de memoria RAM DDR para computadoras de escritorio", 25, 200),
        new("Placa madre", "MBD", "Placa base compatible con procesadores de escritorio", 70, 400),
        new("Fuente de poder", "FNT", "Fuente de alimentación para equipos de escritorio", 30, 220),
        new("Micrófono", "MIC", "Micrófono USB para grabaciones, podcasts y videollamadas", 15, 300),
        new("Reloj inteligente", "SMW", "Reloj inteligente con monitoreo de actividad y notificaciones", 40, 450),
        new("Hub USB", "HUB", "Concentrador USB para ampliar los puertos de la computadora", 10, 90)
    ];

    public static readonly string[] Brands =
    [
        "Samsung", "LG", "HP", "Dell", "Lenovo", "Asus", "Acer", "Logitech", "Razer", "Sony",
        "JBL", "Xiaomi", "TP-Link", "Kingston", "Seagate", "Western Digital", "Canon", "Epson", "Apple", "Huawei"
    ];

    // Solo uno: con los tres fijos del enunciado suman cuatro y, en la demo, se crea el quinto a mano.
    // Pocos proveedores mantienen estrecha la matriz de precios, que crece una columna por proveedor.
    public static readonly SampleSupplier[] Suppliers =
    [
        new("Importadora Pichincha Tech S.A.", "pichinchatech.com.ec")
    ];
}
