using System;
using System.Linq;
using TurnMatch;

namespace TestProject;

/// <summary>
/// Pruebas de las reglas de la partida. No tocan Cloud Save ni el contexto de
/// ejecución: por eso corren en medio segundo y sin servidor.
/// </summary>
public class MatchRulesTests
{
    private const string Host = "player-host";
    private const string Guest = "player-guest";

    private static readonly DateTime Now = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    private static MatchState PartidaEnJuego()
    {
        var state = new MatchState
        {
            MatchCode = "K7QM",
            HostPlayerId = Host,
            Status = MatchStatus.WaitingForGuest
        };

        MatchRules.StartWithGuest(state, Guest, Now);
        return state;
    }

    [Test]
    public void ElPrimerTurnoEsDelAnfitrion()
    {
        var state = PartidaEnJuego();

        Assert.That(state.Status, Is.EqualTo(MatchStatus.Playing));
        Assert.That(state.TurnNumber, Is.EqualTo(1));
        Assert.That(state.CurrentPlayerId, Is.EqualTo(Host));
    }

    [Test]
    public void PasarTurnoAvanzaElContadorYCambiaDeJugador()
    {
        var state = PartidaEnJuego();

        MatchRules.ApplyTurn(state, Host, "req-1", Now);

        Assert.That(state.TurnNumber, Is.EqualTo(2));
        Assert.That(state.CurrentPlayerId, Is.EqualTo(Guest));
        Assert.That(state.History, Has.Count.EqualTo(1));
        Assert.That(state.History[0].PlayerId, Is.EqualTo(Host));
    }

    // --- Idempotencia: el corazón del PoC ---------------------------------

    [Test]
    public void UnaPeticionAplicadaQuedaRegistrada()
    {
        var state = PartidaEnJuego();

        Assert.That(MatchRules.FindProcessed(state, "req-1"), Is.Null);

        MatchRules.ApplyTurn(state, Host, "req-1", Now);

        var processed = MatchRules.FindProcessed(state, "req-1");
        Assert.That(processed, Is.Not.Null);
        Assert.That(processed!.ResultingTurnNumber, Is.EqualTo(2));
        Assert.That(processed.PlayerId, Is.EqualTo(Host));
    }

    [Test]
    public void ReintentarLaMismaPeticionNoDebeAplicarseDosVeces()
    {
        var state = PartidaEnJuego();

        MatchRules.ApplyTurn(state, Host, "req-1", Now);
        var turnoTrasElPrimerEnvio = state.TurnNumber;

        // Así es como el módulo trata un reintento: mira si ya está procesado
        // ANTES de validar nada, y si lo está no toca el estado.
        var processed = MatchRules.FindProcessed(state, "req-1");
        if (processed == null)
        {
            MatchRules.ApplyTurn(state, Host, "req-1", Now);
        }

        Assert.That(state.TurnNumber, Is.EqualTo(turnoTrasElPrimerEnvio));
        Assert.That(state.History, Has.Count.EqualTo(1), "el turno se anotó dos veces");
    }

    [Test]
    public void UnReintentoLegitimoSeriaRechazadoSiSeValidaraAntesDeMirarLaIdempotencia()
    {
        // Este test documenta por qué el orden importa. Tras aplicar el turno del
        // anfitrión, su propio reintento ya no pasa la validación: el turno es del
        // rival. Si el módulo validara antes de comprobar la idempotencia, le diría
        // "no es tu turno" a alguien que sí jugó, y se quedaría sin saber si contó.
        var state = PartidaEnJuego();
        MatchRules.ApplyTurn(state, Host, "req-1", Now);

        var verdict = MatchRules.ValidateTurn(state, Host, expectedTurnNumber: 1);

        Assert.That(verdict, Is.Not.EqualTo(TurnOutcome.Applied));
        Assert.That(MatchRules.FindProcessed(state, "req-1"), Is.Not.Null,
            "la idempotencia es lo único que salva este caso");
    }

    [Test]
    public void ElRegistroDePeticionesNoCreceSinLimite()
    {
        var state = PartidaEnJuego();

        for (var i = 0; i < MatchRules.MaxProcessedRequests + 10; i++)
        {
            var jugador = state.CurrentPlayerId;
            MatchRules.ApplyTurn(state, jugador, $"req-{i}", Now);
        }

        Assert.That(state.ProcessedRequests, Has.Count.EqualTo(MatchRules.MaxProcessedRequests));
        Assert.That(state.ProcessedRequests.Last().RequestId, Is.EqualTo($"req-{MatchRules.MaxProcessedRequests + 9}"));
    }

    // --- Validación --------------------------------------------------------

    [Test]
    public void NoSePuedeJugarAntesDeQueSeUnaElRival()
    {
        var state = new MatchState { MatchCode = "K7QM", HostPlayerId = Host };

        Assert.That(MatchRules.ValidateTurn(state, Host, 0), Is.EqualTo(TurnOutcome.NotStarted));
    }

    [Test]
    public void UnClienteConElEstadoViejoEsRechazado()
    {
        var state = PartidaEnJuego();
        MatchRules.ApplyTurn(state, Host, "req-1", Now);

        // El invitado cree que sigue el turno 1, pero ya vamos por el 2.
        Assert.That(MatchRules.ValidateTurn(state, Guest, expectedTurnNumber: 1),
            Is.EqualTo(TurnOutcome.Stale));
    }

    [Test]
    public void JugarFueraDeTurnoEsRechazado()
    {
        var state = PartidaEnJuego();

        Assert.That(MatchRules.ValidateTurn(state, Guest, expectedTurnNumber: 1),
            Is.EqualTo(TurnOutcome.NotYourTurn));
    }

    [Test]
    public void ElTurnoValidoSeAcepta()
    {
        var state = PartidaEnJuego();

        Assert.That(MatchRules.ValidateTurn(state, Host, expectedTurnNumber: 1),
            Is.EqualTo(TurnOutcome.Applied));
    }

    // --- Vista para el cliente ---------------------------------------------

    [Test]
    public void LaVistaDiceACadaJugadorSiEsSuTurno()
    {
        var state = PartidaEnJuego();

        var vistaHost = MatchRules.BuildView(state, Host, TurnOutcome.Ok, "", Now);
        var vistaGuest = MatchRules.BuildView(state, Guest, TurnOutcome.Ok, "", Now);

        Assert.That(vistaHost.IsYourTurn, Is.True);
        Assert.That(vistaHost.OpponentPlayerId, Is.EqualTo(Guest));

        Assert.That(vistaGuest.IsYourTurn, Is.False);
        Assert.That(vistaGuest.OpponentPlayerId, Is.EqualTo(Host));
    }
}

/// <summary>
/// El código de partida se deriva del identificador de la petición, y de ahí sale
/// la idempotencia de "crear partida".
/// </summary>
public class MatchCodeTests
{
    [Test]
    public void LaMismaPeticionSiempreDaElMismoCodigo()
    {
        Assert.That(MatchCode.FromRequestId("abc-123"), Is.EqualTo(MatchCode.FromRequestId("abc-123")));
    }

    [Test]
    public void PeticionesDistintasDanCodigosDistintos()
    {
        Assert.That(MatchCode.FromRequestId("abc-123"), Is.Not.EqualTo(MatchCode.FromRequestId("abc-124")));
    }

    [Test]
    public void CadaIntentoBuscaUnCodigoNuevo()
    {
        var primero = MatchCode.FromRequestId("abc-123", attempt: 0);
        var segundo = MatchCode.FromRequestId("abc-123", attempt: 1);

        Assert.That(segundo, Is.Not.EqualTo(primero));
    }

    [Test]
    public void ElCodigoEvitaCaracteresQueSeConfundenAlDictarlo()
    {
        for (var i = 0; i < 500; i++)
        {
            var code = MatchCode.FromRequestId($"peticion-{i}");

            Assert.That(code, Has.Length.EqualTo(MatchCode.Length));
            Assert.That(code.Intersect("IO01"), Is.Empty, $"el código {code} tiene caracteres ambiguos");
            Assert.That(MatchCode.IsWellFormed(code), Is.True);
        }
    }

    [Test]
    public void SeAceptaLoQueElJugadorTecleaEnMinusculasYConEspacios()
    {
        Assert.That(MatchCode.Normalize("  k7qm "), Is.EqualTo("K7QM"));
        Assert.That(MatchCode.IsWellFormed(" k7qm "), Is.True);
    }

    [Test]
    public void SeRechazaUnCodigoConFormatoRaro()
    {
        Assert.That(MatchCode.IsWellFormed("K7Q"), Is.False);
        Assert.That(MatchCode.IsWellFormed("K7QMM"), Is.False);
        Assert.That(MatchCode.IsWellFormed("K7Q!"), Is.False);
        Assert.That(MatchCode.IsWellFormed("K7Q0"), Is.False);
        Assert.That(MatchCode.IsWellFormed(null), Is.False);
    }
}
