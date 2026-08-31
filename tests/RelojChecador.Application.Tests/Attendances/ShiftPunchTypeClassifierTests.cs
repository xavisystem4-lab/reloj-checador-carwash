using RelojChecador.Application.Attendances;

namespace RelojChecador.Application.Tests.Attendances;

public class ShiftPunchTypeClassifierTests
{
    private static DateTime At(int hour, int minute) => new(2026, 8, 31, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void Classify_SinMarcacionesPrevias_EsEntrada()
    {
        var result = ShiftPunchTypeClassifier.Classify([], At(8, 0));

        Assert.Equal(ShiftPunchTypeClassifier.EntradaCode, result);
    }

    [Fact]
    public void Classify_SegundaChecadaDespuesDe7h50_EsSalida()
    {
        var previas = new[] { (At(8, 0), (int?)ShiftPunchTypeClassifier.EntradaCode) };

        var result = ShiftPunchTypeClassifier.Classify(previas, At(15, 50)); // exacto 7h50

        Assert.Equal(ShiftPunchTypeClassifier.SalidaCode, result);
    }

    [Fact]
    public void Classify_SegundaChecadaUnMinutoAntesDe7h50_SigueSiendoEntrada()
    {
        var previas = new[] { (At(8, 0), (int?)ShiftPunchTypeClassifier.EntradaCode) };

        var result = ShiftPunchTypeClassifier.Classify(previas, At(15, 49));

        Assert.Equal(ShiftPunchTypeClassifier.EntradaCode, result);
    }

    [Fact]
    public void Classify_TerceraChecadaCuentaDesdeLaEntradaOriginal_NoDesdeLaSegunda()
    {
        // Entrada a las 8:00, una checada de más a las 10:00 (sigue "Entrada" porque no
        // pasaron 7h50) — la que de verdad cierra el turno debe contar desde las 8:00, no
        // desde las 10:00.
        var previas = new[]
        {
            (At(8, 0), (int?)ShiftPunchTypeClassifier.EntradaCode),
            (At(10, 0), (int?)ShiftPunchTypeClassifier.EntradaCode),
        };

        var result = ShiftPunchTypeClassifier.Classify(previas, At(15, 55)); // 7h55 desde las 8:00

        Assert.Equal(ShiftPunchTypeClassifier.SalidaCode, result);
    }

    [Fact]
    public void Classify_DespuesDeUnaSalida_LaSiguienteEsEntradaNueva()
    {
        var previas = new[]
        {
            (At(8, 0), (int?)ShiftPunchTypeClassifier.EntradaCode),
            (At(15, 55), (int?)ShiftPunchTypeClassifier.SalidaCode),
        };

        var result = ShiftPunchTypeClassifier.Classify(previas, At(16, 30));

        Assert.Equal(ShiftPunchTypeClassifier.EntradaCode, result);
    }

    [Fact]
    public void Classify_MarcacionViejaSinClasificar_SeTrataComoEntrada()
    {
        var previas = new[] { (At(8, 0), (int?)null) };

        var result = ShiftPunchTypeClassifier.Classify(previas, At(15, 55));

        Assert.Equal(ShiftPunchTypeClassifier.SalidaCode, result);
    }

    [Fact]
    public void Classify_SoloSeConsideranMarcacionesDelMismoDia_LlamadorDebeFiltrarlas()
    {
        // El método en sí no filtra por día — eso es responsabilidad de quien arma
        // todaysPunchesBeforeNew (ver DevicesViewModel) — pero si SOLO le pasan las de hoy
        // (simulando que ayer nunca cerró), una entrada de hoy sigue siendo Entrada.
        var soloHoy = Array.Empty<(DateTime, int?)>();

        var result = ShiftPunchTypeClassifier.Classify(soloHoy, At(8, 5));

        Assert.Equal(ShiftPunchTypeClassifier.EntradaCode, result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Regla de horario — pedido explícito del usuario: "cuando cumpla el ciclo o su horario
    // que diga salida". Cubre el turno CORTO (menor a 7h50), que sin esto nunca cerraría
    // solo con la regla del ciclo.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Classify_TurnoCortoAlcanzaSuHoraDeSalidaProgramada_EsSalidaAunqueNoLleguenLas7h50()
    {
        // Turno de 8:00 a 13:00 (5 horas) — nunca llega a las 7h50, solo la hora
        // programada puede cerrarlo.
        var previas = new[] { (At(8, 0), (int?)ShiftPunchTypeClassifier.EntradaCode) };
        var scheduledEndTime = new TimeOnly(13, 0);

        var result = ShiftPunchTypeClassifier.Classify(previas, At(13, 0), scheduledEndTime);

        Assert.Equal(ShiftPunchTypeClassifier.SalidaCode, result);
    }

    [Fact]
    public void Classify_TurnoCortoAntesDeSuHoraDeSalidaProgramada_SigueSiendoEntrada()
    {
        var previas = new[] { (At(8, 0), (int?)ShiftPunchTypeClassifier.EntradaCode) };
        var scheduledEndTime = new TimeOnly(13, 0);

        var result = ShiftPunchTypeClassifier.Classify(previas, At(12, 59), scheduledEndTime);

        Assert.Equal(ShiftPunchTypeClassifier.EntradaCode, result);
    }

    [Fact]
    public void Classify_SinHorarioCapturado_SoloAplicaLaRegladel7h50()
    {
        // scheduledEndTime null (omitido) — mismo comportamiento que antes de que existiera
        // Horario de empleados, ver el resto de esta clase de pruebas.
        var previas = new[] { (At(8, 0), (int?)ShiftPunchTypeClassifier.EntradaCode) };

        var result = ShiftPunchTypeClassifier.Classify(previas, At(13, 0));

        Assert.Equal(ShiftPunchTypeClassifier.EntradaCode, result);
    }

    [Fact]
    public void Classify_TurnoLargoCumpleLas7h50AntesDeSuHorario_EsSalidaPorElCiclo()
    {
        // Turno de 8:00 a 20:00 (12 horas) — las 7h50 (15:50) llegan mucho antes que la
        // hora programada (20:00): cualquiera de las dos condiciones basta.
        var previas = new[] { (At(8, 0), (int?)ShiftPunchTypeClassifier.EntradaCode) };
        var scheduledEndTime = new TimeOnly(20, 0);

        var result = ShiftPunchTypeClassifier.Classify(previas, At(15, 50), scheduledEndTime);

        Assert.Equal(ShiftPunchTypeClassifier.SalidaCode, result);
    }
}
