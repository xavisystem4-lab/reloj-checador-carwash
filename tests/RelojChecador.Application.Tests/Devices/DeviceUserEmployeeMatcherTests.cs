using RelojChecador.Application.Devices;
using RelojChecador.Domain.Employees;

namespace RelojChecador.Application.Tests.Devices;

public class DeviceUserEmployeeMatcherTests
{
    private static Employee Emp(string number, string name, EmploymentStatus? status = null)
    {
        var employee = Employee.Create(
            EmployeeNumber.Create(number), name, Guid.NewGuid(), new DateOnly(2025, 1, 1), 2500m);
        if (status is { } s && employee.Status != s)
        {
            employee.ChangeStatus(s);
        }
        return employee;
    }

    private static PinSlot Slot(string pin, string deviceName, Employee? owner = null) =>
        new(pin, owner is null ? [deviceName] : [deviceName, owner.FullName], owner?.Id);

    private static PinMatchResult Single(PinSlot slot, params Employee[] employees) =>
        Assert.Single(DeviceUserEmployeeMatcher.Match([slot], employees));

    [Fact]
    public void NombreDelReloj_MismasPalabrasSinImportarAcentosNiMayusculas_Vincula()
    {
        var jose = Emp("EMP-1", "José  Pérez López");

        var result = Single(Slot("7", "JOSE PEREZ LOPEZ"), jose, Emp("EMP-2", "Ana Torres"));

        Assert.True(result.ShouldLink);
        Assert.Equal(jose.Id, result.Employee!.Id);
        Assert.Equal(PinMatchKind.NameMatch, result.Kind);
    }

    [Fact]
    public void NombreCortoDelCatalogo_SeEmparejaConElNombreCompletoDelReloj()
    {
        var emp = Emp("EMP-037", "Adali");

        var result = Single(Slot("38", "Adali Monserrat Tabanico Ramos"), emp);

        Assert.True(result.ShouldLink);
        Assert.Equal(emp.Id, result.Employee!.Id);
    }

    [Fact]
    public void PalabrasNoConsecutivas_PeroEnOrden_Coinciden()
    {
        var emp = Emp("EMP-010", "Antony Beltran");

        var result = Single(Slot("9", "Antony Salvador Beltran Garcia"), emp);

        Assert.True(result.ShouldLink);
    }

    [Fact]
    public void PalabrasEnOtroOrden_NoCoinciden()
    {
        var result = Single(Slot("9", "Beltran Antony"), Emp("EMP-010", "Antony Beltran"));

        Assert.False(result.ShouldLink);
    }

    [Fact]
    public void EmpleadoDeBaja_DuenoDelPin_SeTraspasaAlEmpleadoVigente()
    {
        var viejo = Emp("38", "Adali Monserrat Tabanico Ramos", EmploymentStatus.Terminated);
        var nuevo = Emp("EMP-037", "Adali");

        var result = Single(Slot("38", "", viejo), viejo, nuevo);

        Assert.True(result.ShouldLink);
        Assert.Equal(nuevo.Id, result.Employee!.Id);
        Assert.Equal(viejo.Id, result.Slot.MappedEmployeeId);
    }

    [Fact]
    public void PinDeEmpleadoVigente_NoSeToca()
    {
        var vigente = Emp("EMP-037", "Adali");

        var result = Single(Slot("38", "Adali Monserrat", vigente), vigente, Emp("EMP-099", "Adali Monserrat"));

        Assert.False(result.ShouldLink);
        Assert.Equal(PinMatchKind.AlreadyLinked, result.Kind);
    }

    [Fact]
    public void EmpleadoQueYaTienePinVigente_NoSeVuelveAVincular()
    {
        var conPin = Emp("EMP-001", "Adrian Uribe");
        var slots = new[]
        {
            new PinSlot("1", ["Adrian Uribe Garcia"], conPin.Id),
            new PinSlot("2", ["Adrian Uribe Garcia"], null), // otro PIN con el mismo nombre
        };

        var results = DeviceUserEmployeeMatcher.Match(slots, [conPin]);

        Assert.Equal(PinMatchKind.AlreadyLinked, results[0].Kind);
        Assert.False(results[1].ShouldLink);
    }

    [Fact]
    public void MasEspecificoGana_Isaac_vs_IsaacRojo()
    {
        var isaac = Emp("EMP-009", "Isaac");
        var isaacRojo = Emp("EMP-012", "Isaac Rojo");

        var results = DeviceUserEmployeeMatcher.Match([Slot("11", "Isaac Ramon Rojo Torres")], [isaac, isaacRojo]);

        Assert.Equal(isaacRojo.Id, results[0].Employee!.Id);
    }

    [Fact]
    public void NombreQueCabeEnDosPins_SinDesempate_NoAdivina()
    {
        var pablo = Emp("EMP-019", "Pablo");
        var slots = new[] { Slot("18", "Pablo Rodrigo Aguilera"), Slot("30", "Jose Pablo Rojo") };

        var results = DeviceUserEmployeeMatcher.Match(slots, [pablo]);

        Assert.All(results, r => Assert.False(r.ShouldLink));
        Assert.All(results, r => Assert.Equal(PinMatchKind.Ambiguous, r.Kind));
    }

    [Fact]
    public void Encadenado_PabloSeResuelveCuandoJosePabloRojoTomaSuPin()
    {
        var pablo = Emp("EMP-019", "Pablo");
        var josePablo = Emp("EMP-031", "Jose Pablo Rojo");
        var slots = new[] { Slot("18", "Pablo Rodrigo Aguilera"), Slot("30", "Jose Pablo Rojo Torres") };

        var results = DeviceUserEmployeeMatcher.Match(slots, [pablo, josePablo]);

        Assert.Equal(pablo.Id, results[0].Employee!.Id);
        Assert.Equal(josePablo.Id, results[1].Employee!.Id);
    }

    [Fact]
    public void Homonimos_NoAdivina()
    {
        var results = DeviceUserEmployeeMatcher.Match(
            [Slot("5", "Juan Lopez")], [Emp("EMP-1", "Juan Lopez"), Emp("EMP-2", "Juan Lopez")]);

        Assert.False(results[0].ShouldLink);
        Assert.Equal(PinMatchKind.Ambiguous, results[0].Kind);
    }

    [Fact]
    public void SinNombreQueCoincida_CaeAlNumeroDeEmpleadoIgualAlPin_IgnorandoCeros()
    {
        var emp = Emp("0114", "Roberto Diaz");

        var result = Single(Slot("114", "R. D."), emp);

        Assert.True(result.ShouldLink);
        Assert.Equal(PinMatchKind.EmployeeNumber, result.Kind);
    }

    [Fact]
    public void NumeroEmpNNN_NoSeUsaComoPin()
    {
        // En un catálogo renumerado "EMP-007" ya no es el PIN 7 (Andrés Herrera tiene el 6):
        // nunca se debe deducir el PIN del sufijo del número.
        var result = Single(Slot("7", "Jesus Guadalupe Lopez Montoya"), Emp("EMP-007", "Andres Herrera"));

        Assert.False(result.ShouldLink);
    }

    [Fact]
    public void EmpleadoDadoDeBaja_NuncaSeVincula()
    {
        var baja = Emp("12", "Jose Perez", EmploymentStatus.Terminated);

        var result = Single(Slot("7", "Jose Perez"), baja);

        Assert.False(result.ShouldLink);
    }

    // ---- PINs "de relleno": asignados por "Enviar empleados al reloj", sin ninguna checada ----

    [Fact]
    public void PinDeRelleno_SeCambiaPorElPinRealConLasChecadas()
    {
        var viejo = Emp("38", "Adali Monserrat Tabanico Ramos", EmploymentStatus.Terminated);
        var nuevo = Emp("EMP-037", "Adali");
        var slots = new[]
        {
            new PinSlot("38", ["Adali Monserrat Tabanico Ramos"], viejo.Id, HasPunches: true),
            new PinSlot("94", ["Adali", "Adali"], nuevo.Id, HasPunches: false), // relleno de hoy
        };

        var results = DeviceUserEmployeeMatcher.Match(slots, [viejo, nuevo]);

        Assert.True(results[0].ShouldLink);
        Assert.Equal(nuevo.Id, results[0].Employee!.Id);
        Assert.Equal(PinMatchKind.Released, results[1].Kind);
        Assert.Equal(nuevo.Id, results[1].Employee!.Id);
    }

    [Fact]
    public void PinDeRelleno_SinMejorCandidato_SeQuedaComoEsta()
    {
        var nuevo = Emp("EMP-051", "Ana Laura");

        var results = DeviceUserEmployeeMatcher.Match(
            [new PinSlot("107", ["Ana Laura"], nuevo.Id, HasPunches: false)], [nuevo]);

        Assert.Equal(PinMatchKind.AlreadyLinked, results[0].Kind);
    }

    [Fact]
    public void PinConChecadas_DeEmpleadoVigente_NuncaSeMueve()
    {
        var vigente = Emp("EMP-043", "Miguel Sauceda");
        var otro = Emp("77", "Miguel Sauceda Lopez", EmploymentStatus.Terminated);
        var slots = new[]
        {
            new PinSlot("36", ["Miguel Sauceda"], vigente.Id, HasPunches: true),
            new PinSlot("77", ["Miguel Sauceda Lopez"], otro.Id, HasPunches: true),
        };

        var results = DeviceUserEmployeeMatcher.Match(slots, [vigente, otro]);

        Assert.Equal(PinMatchKind.AlreadyLinked, results[0].Kind);
        Assert.False(results[1].ShouldLink);
    }

    [Fact]
    public void PinDeRelleno_NuncaEsDestinoDeOtroEmpleado()
    {
        // El PIN 60 es de relleno de "Juan Perez"; "Juan Perez Lopez" no debe caer ahí.
        var juan = Emp("EMP-1", "Juan Perez");
        var juanLopez = Emp("EMP-2", "Juan Perez Lopez");
        var slots = new[] { new PinSlot("60", ["Juan Perez"], juan.Id, HasPunches: false) };

        var results = DeviceUserEmployeeMatcher.Match(slots, [juan, juanLopez]);

        Assert.False(results[0].ShouldLink);
    }

    // ---- Caso real (18/09/2026): catálogo reemplazado, 54 nuevos vs 59 anteriores dados de baja ----

    private static readonly (string Number, string Name)[] NewCatalog =
    [
        ("EMP-037", "Adali"), ("EMP-001", "Adrian Uribe"), ("EMP-003", "Alexander Santiago"), ("EMP-017", "Alexis Valencia"),
        ("EMP-051", "Ana Laura"), ("EMP-007", "Andres Herrera"), ("EMP-002", "Angel David"), ("EMP-028", "Angel Norzagaray"),
        ("EMP-010", "Antony Beltran"), ("EMP-006", "Armando"), ("EMP-040", "Berenice"), ("EMP-048", "Brandon"),
        ("EMP-035", "Brayan Joseth"), ("EMP-029", "Bryan Francisco"), ("EMP-032", "Daniel Gonzalez"), ("EMP-004", "David Tapia"),
        ("EMP-023", "Eduardo Castillo"), ("EMP-030", "Emilio Villegas"), ("EMP-045", "Ernesto"), ("EMP-020", "Fabian"),
        ("EMP-021", "Felix Josue"), ("EMP-052", "Fernanda"), ("EMP-026", "Gabriel"), ("EMP-009", "Isaac"),
        ("EMP-012", "Isaac Rojo"), ("EMP-053", "Israel"), ("EMP-022", "Ivan Martinez"), ("EMP-039", "Janeth Moreno"),
        ("EMP-050", "Javier Dominguez"), ("EMP-016", "Jesus Adrian"), ("EMP-008", "Jesus Lopez"), ("EMP-027", "Jesus Rios"),
        ("EMP-042", "Jhonny"), ("EMP-011", "Jorge Luis"), ("EMP-031", "Jose Pablo Rojo"), ("EMP-047", "Juanito"),
        ("EMP-046", "Julia"), ("EMP-024", "Kevin Antonio"), ("EMP-018", "Kevin Ruiz"), ("EMP-044", "Leslie"),
        ("EMP-034", "Luis Alfonso"), ("EMP-033", "Luis Luna"), ("EMP-043", "Miguel Sauceda"), ("EMP-041", "Nathalia"),
        ("EMP-038", "Nayeli Barreras"), ("EMP-025", "Oswaldo Tapia"), ("EMP-019", "Pablo"), ("EMP-054", "Paola"),
        ("EMP-014", "Pedro Ivan"), ("EMP-049", "Ricardo"), ("EMP-005", "Rodolfo Romo"), ("EMP-015", "Salvador Tellez"),
        ("EMP-013", "Sebastian Jimenez"), ("EMP-036", "Yajaira"),
    ];

    private static readonly (string Pin, string Name)[] OldCatalog =
    [
        ("38", "Adali Monserrat Tabanico Ramos"), ("1", "Adrian Uribe Garcia"), ("3", "Alexander Rfael Santiago Garcia"),
        ("49", "Alma Julia Sanchez Guerrero"), ("6", "Andres Mateo Herrera Leyva"), ("56", "Andrick Barra"),
        ("31", "Angel Daniel Gonzalez Hernandez"), ("2", "Angel David Gutierrez Gonzalez"),
        ("25", "Angel Gabriel De La Torre Gutierrez"), ("41", "Angela Janeth Verdugo Moreno"),
        ("9", "Antony Salvador Beltran Garcia"), ("42", "Berenice Cisneros Estarda"), ("47", "Brandon Manuel Araiza Castro"),
        ("34", "Brayan Joseth Flores Ovalles"), ("28", "Bryan Francisco Rosas Soto"), ("53", "Cristofer Aurelio Leyva Guzman"),
        ("21", "Cristoper Ivan"), ("4", "David Salvador Tapia Castro"), ("57", "Edgar Ramirez"), ("22", "Eduardo Castillo Flores"),
        ("55", "Edwin Torres"), ("29", "Emilio Villegas Bañuelos"), ("54", "Ernesto Guadalupe Valenzuela Felix"),
        ("59", "Esteban Leon Lopez"), ("16", "Hugo Alexis Valencia Estrella"), ("11", "Isaac Ramon Rojo Torres"),
        ("46", "Israel Maya Martinez"), ("51", "Javier Alexander Dominguez Jara"), ("201", "Javier Galaviz"),
        ("15", "Jesus Adrian Calderon Graciano"), ("7", "Jesus Guadalupe Lopez Montoya"), ("26", "Jesus Jael Rios Chavez"),
        ("35", "Jhonny Steven Aranguren Hidalgo"), ("10", "Jorge Luis Rodriguez Algandar"), ("30", "Jose Pablo Rojo Torres"),
        ("48", "Juan Gonzalez Avila"), ("23", "Kevin Antonio Guardado Soto"), ("19", "Kevin Favian Rodriguez"),
        ("17", "Kevin Uziel Mendoza Mendoza"), ("37", "Leslie Michelle Beltran Castellanos"), ("33", "Luis Alfonso Soto Trujillo"),
        ("12", "Luis Angel Gonzalez Ramos"), ("32", "Luis Fernado Luna Rojas"), ("45", "Maria Fernanda De La Cruz Sanchez"),
        ("43", "Nathalia Trujillo Figueroa"), ("40", "Nayeli Guadalupe Guia Barreras"), ("24", "Oswaldo Tapia Castro"),
        ("18", "Pablo Rodrigo Aguilera Del Valle"), ("13", "Pedro Ivan Leon Rodriguez"), ("5", "Pedro Rodolfo Navarrete Romo"),
        ("58", "Ramses Barajas"), ("50", "Ricardo Payan Lopez"), ("52", "Romina Moreno Gonzalez"),
        ("14", "Salvador Tellez Valencia"), ("44", "Sandra Paola Ulloa Castellanos"), ("8", "Sebastian Jimenez Cortez"),
        ("39", "Valeria Martinez Longoria"),
    ];

    [Fact]
    public void CasoReal_CatalogoReemplazado_ResuelveCadaPinAlEmpleadoCorrectoYNuncaAdivina()
    {
        var newEmployees = NewCatalog.Select(e => Emp(e.Number, e.Name)).ToList();
        var oldEmployees = OldCatalog.Select(o => (o.Pin, Employee: Emp(o.Pin, o.Name, EmploymentStatus.Terminated))).ToList();
        var slots = oldEmployees.Select(o => new PinSlot(o.Pin, [o.Employee.FullName], o.Employee.Id)).ToList();

        var results = DeviceUserEmployeeMatcher.Match(slots, [.. newEmployees, .. oldEmployees.Select(o => o.Employee)]);
        var pinByName = results.Where(r => r.ShouldLink).ToDictionary(r => r.Employee!.FullName, r => r.Slot.Pin);

        // Pares que sí deben resolverse (verificados contra el catálogo real).
        var expected = new Dictionary<string, string>
        {
            ["Adali"] = "38", ["Adrian Uribe"] = "1", ["Alexander Santiago"] = "3", ["Alexis Valencia"] = "16",
            ["Andres Herrera"] = "6", ["Angel David"] = "2", ["Antony Beltran"] = "9", ["Berenice"] = "42",
            ["Brandon"] = "47", ["Brayan Joseth"] = "34", ["Bryan Francisco"] = "28", ["Daniel Gonzalez"] = "31",
            ["David Tapia"] = "4", ["Eduardo Castillo"] = "22", ["Emilio Villegas"] = "29", ["Ernesto"] = "54",
            ["Fernanda"] = "45", ["Gabriel"] = "25", ["Isaac Rojo"] = "11", ["Israel"] = "46", ["Janeth Moreno"] = "41",
            ["Javier Dominguez"] = "51", ["Jesus Adrian"] = "15", ["Jesus Lopez"] = "7", ["Jesus Rios"] = "26",
            ["Jhonny"] = "35", ["Jorge Luis"] = "10", ["Jose Pablo Rojo"] = "30", ["Julia"] = "49", ["Kevin Antonio"] = "23",
            ["Leslie"] = "37", ["Luis Alfonso"] = "33", ["Luis Luna"] = "32", ["Nathalia"] = "43", ["Nayeli Barreras"] = "40",
            ["Oswaldo Tapia"] = "24", ["Pablo"] = "18", ["Paola"] = "44", ["Pedro Ivan"] = "13", ["Ricardo"] = "50",
            ["Rodolfo Romo"] = "5", ["Salvador Tellez"] = "14", ["Sebastian Jimenez"] = "8",
        };
        foreach (var (name, pin) in expected)
        {
            Assert.True(pinByName.TryGetValue(name, out var actualPin), $"{name} debía vincularse al PIN {pin} y quedó sin vincular");
            Assert.Equal(pin, actualPin);
        }

        // Sin PIN posible en el reloj: no se debe inventar ninguno.
        foreach (var name in new[] { "Ana Laura", "Armando", "Fabian", "Isaac", "Ivan Martinez", "Kevin Ruiz", "Yajaira", "Juanito" })
        {
            Assert.False(pinByName.ContainsKey(name), $"{name} no tiene PIN en el reloj y no debía vincularse");
        }

        // Ningún PIN se asigna a dos empleados ni un empleado a dos PINs.
        Assert.Equal(pinByName.Count, pinByName.Values.Distinct().Count());
    }

    [Fact]
    public void CasoReal_ConPinesDeRellenoDeHoy_CadaEmpleadoPasaDeSuPin60a110AlPinViejoConLasChecadas()
    {
        // Estado real de Supabase (18/09/2026 18:15 UTC): los 54 vigentes tienen PINs 60..110
        // (sin ninguna checada); los viejos (1..59) siguen a nombre de empleados dados de baja
        // y son donde están las 1262 checadas.
        var newEmployees = NewCatalog.Select(e => Emp(e.Number, e.Name)).ToList();
        var oldEmployees = OldCatalog.Select(o => (o.Pin, Employee: Emp(o.Pin, o.Name, EmploymentStatus.Terminated))).ToList();

        var slots = oldEmployees
            .Select(o => new PinSlot(o.Pin, [o.Employee.FullName], o.Employee.Id, HasPunches: true))
            .ToList();
        var nextPin = 60;
        foreach (var employee in newEmployees)
        {
            slots.Add(new PinSlot((nextPin++).ToString(), [employee.FullName, employee.FullName], employee.Id, HasPunches: false));
        }

        var results = DeviceUserEmployeeMatcher.Match(slots, [.. newEmployees, .. oldEmployees.Select(o => o.Employee)]);

        var moved = results.Where(r => r.ShouldLink).ToDictionary(r => r.Employee!.FullName, r => r.Slot.Pin);
        Assert.Equal("38", moved["Adali"]);
        Assert.Equal("6", moved["Andres Herrera"]);   // EMP-007, PIN real 6 (no 7)
        Assert.Equal("11", moved["Isaac Rojo"]);
        // 43 resueltos + 8 sin PIN posible + 3 que ya checan con su PIN real (Miguel Sauceda,
        // Felix Josue, Angel Norzagaray) = los 54 vigentes.
        Assert.True(moved.Count == 43, string.Join(", ", moved.Select(kv => $"{kv.Key}->{kv.Value}")));

        // Cada empleado que se mudó libera su PIN de relleno; el resto se queda con el suyo.
        var released = results.Where(r => r.Kind == PinMatchKind.Released).Select(r => r.Employee!.FullName).ToHashSet();
        Assert.Equal(moved.Keys.ToHashSet(), released);
        Assert.DoesNotContain(results, r => r.Kind == PinMatchKind.Released && !moved.ContainsKey(r.Employee!.FullName));

        // Los que no tienen PIN viejo se quedan con su PIN de relleno, sin tocar.
        foreach (var name in new[] { "Ana Laura", "Armando", "Fabian", "Isaac", "Ivan Martinez", "Kevin Ruiz", "Yajaira", "Juanito" })
        {
            Assert.False(moved.ContainsKey(name), name);
        }
    }
}
