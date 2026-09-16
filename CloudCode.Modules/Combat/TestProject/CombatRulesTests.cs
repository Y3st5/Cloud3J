using System;
using System.Linq;
using Combat;

namespace TestProject;

/// <summary>
/// Pruebas de las reglas del duelo. No tocan Cloud Save ni el contexto de
/// ejecución: por eso corren en medio segundo y sin servidor.
/// </summary>
public class CombatRulesTests
{
    private const string Player1 = "jugador-uno";
    private const string Player2 = "jugador-dos";

    private static CombatMatch DueloEnJuego()
    {
        return new CombatMatch
        {
            MatchId = "K7QM",
            Player1Id = Player1,
            Player2Id = Player2,
            Status = CombatStatus.Playing,
            Player1HP = CombatRules.MaxHealth,
            Player2HP = CombatRules.MaxHealth,
            CurrentRound = 1
        };
    }

    /// <summary>Juega una ronda completa y devuelve el resultado.</summary>
    private static (CombatMatch Match, RoundResult Result) JugarRonda(
        CombatClass[] p1, CombatClass[] p2)
    {
        var match = DueloEnJuego();
        CombatRules.RecordMoves(match, Player1, p1);
        CombatRules.RecordMoves(match, Player2, p2);
        var result = CombatRules.ResolveRound(match);
        return (match, result);
    }

    // --- Movimientos --------------------------------------------------------

    [Test]
    public void TresMovimientosConocidosSeAceptan()
    {
        var moves = new[] { CombatClass.Warrior, CombatClass.Mage, CombatClass.Assassin };

        Assert.That(CombatRules.AreMovesValid(moves), Is.True);
    }

    [Test]
    public void CualquierMovimientoDesconocidoRechazaLaSecuencia()
    {
        var moves = new[] { CombatClass.Warrior, CombatClass.Unknown, CombatClass.Assassin };

        Assert.That(CombatRules.AreMovesValid(moves), Is.False);
    }

    [Test]
    public void MenosDeTresMovimientosSeRechazan()
    {
        var moves = new[] { CombatClass.Warrior, CombatClass.Mage };

        Assert.That(CombatRules.AreMovesValid(moves), Is.False);
    }

    // --- Depredación ---------------------------------------------------------

    [Test]
    public void ElEnfrentamientoDelMismoTipoEsEmpate()
    {
        var (_, result) = JugarRonda(
            new[] { CombatClass.Warrior, CombatClass.Mage, CombatClass.Assassin },
            new[] { CombatClass.Warrior, CombatClass.Mage, CombatClass.Assassin });

        Assert.That(result.Collisions, Has.Count.EqualTo(CombatRules.MovesPerRound));
        Assert.That(result.Collisions.All(c => c.Result == CollisionResult.Draw), Is.True);
        Assert.That(result.P1DamageTaken, Is.Zero);
        Assert.That(result.P2DamageTaken, Is.Zero);
    }

    [Test]
    public void ElMagoVenceAlGuerrero()
    {
        var (match, result) = JugarRonda(
            new[] { CombatClass.Mage, CombatClass.Mage, CombatClass.Mage },
            new[] { CombatClass.Warrior, CombatClass.Warrior, CombatClass.Warrior });

        Assert.That(result.Collisions[0].Result, Is.EqualTo(CollisionResult.P1Wins));
        Assert.That(result.P2DamageTaken, Is.EqualTo(CombatRules.DamagePerHit * CombatRules.MovesPerRound));
        Assert.That(match.Player2HP, Is.EqualTo(CombatRules.MaxHealth - result.P2DamageTaken));
    }

    [Test]
    public void ElAsesinoVenceAlMago()
    {
        var (_, result) = JugarRonda(
            new[] { CombatClass.Assassin, CombatClass.Assassin, CombatClass.Assassin },
            new[] { CombatClass.Mage, CombatClass.Mage, CombatClass.Mage });

        Assert.That(result.Collisions[0].Result, Is.EqualTo(CollisionResult.P1Wins));
        Assert.That(result.P2DamageTaken, Is.EqualTo(CombatRules.DamagePerHit * CombatRules.MovesPerRound));
    }

    [Test]
    public void ElGuerreroVenceAlAsesino()
    {
        var (_, result) = JugarRonda(
            new[] { CombatClass.Warrior, CombatClass.Warrior, CombatClass.Warrior },
            new[] { CombatClass.Assassin, CombatClass.Assassin, CombatClass.Assassin });

        Assert.That(result.Collisions[0].Result, Is.EqualTo(CollisionResult.P1Wins));
        Assert.That(result.P2DamageTaken, Is.EqualTo(CombatRules.DamagePerHit * CombatRules.MovesPerRound));
    }

    [Test]
    public void LaSecaQuePierdeInfligeElDanoAlQueGana()
    {
        var (_, result) = JugarRonda(
            new[] { CombatClass.Warrior, CombatClass.Warrior, CombatClass.Warrior },
            new[] { CombatClass.Mage, CombatClass.Mage, CombatClass.Mage });

        Assert.That(result.Collisions[0].Result, Is.EqualTo(CollisionResult.P2Wins));
        Assert.That(result.P1DamageTaken, Is.EqualTo(CombatRules.DamagePerHit * CombatRules.MovesPerRound));
        Assert.That(result.P2DamageTaken, Is.Zero);
    }

    // --- Resolución de ronda ------------------------------------------------

    [Test]
    public void ResolverLaRondaAvanzaYVaciaLosMovimientos()
    {
        var match = DueloEnJuego();
        CombatRules.RecordMoves(match, Player1,
            new[] { CombatClass.Warrior, CombatClass.Mage, CombatClass.Assassin });
        CombatRules.RecordMoves(match, Player2,
            new[] { CombatClass.Assassin, CombatClass.Warrior, CombatClass.Mage });
        var result = CombatRules.ResolveRound(match);

        Assert.That(result.RoundNumber, Is.EqualTo(1));
        Assert.That(match.CurrentRound, Is.EqualTo(2));
        Assert.That(match.Player1Moves, Is.Empty);
        Assert.That(match.Player2Moves, Is.Empty);
    }

    [Test]
    public void CuandoUnJugadorLlegaACeroElDueloSeResuelve()
    {
        var match = DueloEnJuego();
        match.Player2HP = CombatRules.DamagePerHit; // al borde del KO y pierde la ronda
        CombatRules.RecordMoves(match, Player1, new[] { CombatClass.Mage, CombatClass.Mage, CombatClass.Mage });
        CombatRules.RecordMoves(match, Player2, new[] { CombatClass.Warrior, CombatClass.Warrior, CombatClass.Warrior });
        CombatRules.ResolveRound(match);

        Assert.That(match.IsResolved, Is.True);
        Assert.That(match.Status, Is.EqualTo(CombatStatus.Resolved));
        Assert.That(match.WinnerId, Is.EqualTo(Player1));
        Assert.That(match.Player2HP, Is.LessThanOrEqualTo(0));
    }

    [Test]
    public void QuienSobreviveNoRecibeDanoAlTerminar()
    {
        var match = DueloEnJuego();
        match.Player2HP = CombatRules.DamagePerHit; // al borde del KO
        CombatRules.RecordMoves(match, Player1, new[] { CombatClass.Mage, CombatClass.Mage, CombatClass.Mage });
        CombatRules.RecordMoves(match, Player2, new[] { CombatClass.Warrior, CombatClass.Warrior, CombatClass.Warrior });
        var result = CombatRules.ResolveRound(match);

        Assert.That(match.IsResolved, Is.True);
        Assert.That(match.WinnerId, Is.EqualTo(Player1));
        Assert.That(match.Player1HP, Is.EqualTo(CombatRules.MaxHealth));
        Assert.That(result.P1DamageTaken, Is.Zero);
    }

    // --- Registro de movimientos ---------------------------------------------

    [Test]
    public void GuardarLaPrimeraSecuenciaNoResuelve()
    {
        var match = DueloEnJuego();

        var resolves = CombatRules.RecordMoves(match, Player1,
            new[] { CombatClass.Warrior, CombatClass.Mage, CombatClass.Assassin });

        Assert.That(resolves, Is.False);
        Assert.That(match.Player1Moves, Has.Count.EqualTo(CombatRules.MovesPerRound));
        Assert.That(match.Player2Moves, Is.Empty);
    }

    [Test]
    public void GuardarLaSegundaSecuenciaResuelveLaRonda()
    {
        var match = DueloEnJuego();
        CombatRules.RecordMoves(match, Player1,
            new[] { CombatClass.Warrior, CombatClass.Mage, CombatClass.Assassin });

        var resolves = CombatRules.RecordMoves(match, Player2,
            new[] { CombatClass.Assassin, CombatClass.Warrior, CombatClass.Mage });

        Assert.That(resolves, Is.True);
    }

    // --- Idempotencia --------------------------------------------------------

    [Test]
    public void UnaPeticionGuardadaSeReconoce()
    {
        var match = DueloEnJuego();
        CombatRules.Remember(match, new ProcessedRequest
        {
            RequestId = "req-1",
            Status = CombatOutcome.TurnSubmitted
        });

        var found = CombatRules.FindProcessed(match, "req-1");

        Assert.That(found, Is.Not.Null);
        Assert.That(found.Status, Is.EqualTo(CombatOutcome.TurnSubmitted));
    }

    [Test]
    public void LaInstantaneaPermiteReconstruirElDuelo()
    {
        var processed = new ProcessedRequest
        {
            RequestId = "req-9",
            SnapshotMatchId = "K7QM",
            SnapshotPlayer1Id = Player1,
            SnapshotPlayer2Id = Player2,
            SnapshotPlayer1HP = 80,
            SnapshotPlayer2HP = 100,
            SnapshotCurrentRound = 2,
            SnapshotStatus = CombatStatus.Playing,
            SnapshotIsResolved = false,
            SnapshotLastMoveTimestamp = 12345
        };

        var rebuilt = CombatRules.SnapshotOf(processed);

        Assert.That(rebuilt.MatchId, Is.EqualTo("K7QM"));
        Assert.That(rebuilt.Player1HP, Is.EqualTo(80));
        Assert.That(rebuilt.CurrentRound, Is.EqualTo(2));
        Assert.That(rebuilt.Status, Is.EqualTo(CombatStatus.Playing));
    }

    [Test]
    public void ElHistorialDePeticionesNoCreceSinLimite()
    {
        var match = DueloEnJuego();
        for (var i = 0; i < CombatRules.MaxProcessedRequests + 5; i++)
        {
            CombatRules.Remember(match, new ProcessedRequest
            {
                RequestId = $"req-{i}",
                Status = CombatOutcome.TurnSubmitted
            });
        }

        Assert.That(match.ProcessedRequests.Count, Is.EqualTo(CombatRules.MaxProcessedRequests));
        Assert.That(match.ProcessedRequests[0].RequestId, Is.EqualTo("req-5"));
    }

    // --- Parseo de los movimientos entrantes ----------------------------------

    [Test]
    public void ElParseoEntiendeLosNombresSinDistinguirMayusculas()
    {
        Assert.That(CombatRules.ParseMove("warrior"), Is.EqualTo(CombatClass.Warrior));
        Assert.That(CombatRules.ParseMove("MAGE"), Is.EqualTo(CombatClass.Mage));
        Assert.That(CombatRules.ParseMove("Assassin"), Is.EqualTo(CombatClass.Assassin));
    }

    [Test]
    public void ElParseoEntiendeLosValoresNumericosDelEnum()
    {
        Assert.That(CombatRules.ParseMove("0"), Is.EqualTo(CombatClass.Warrior));
        Assert.That(CombatRules.ParseMove("1"), Is.EqualTo(CombatClass.Mage));
        Assert.That(CombatRules.ParseMove("2"), Is.EqualTo(CombatClass.Assassin));
    }

    [Test]
    public void ElParseoDevuelveUnknownAnteCualquierBasura()
    {
        Assert.That(CombatRules.ParseMove(null), Is.EqualTo(CombatClass.Unknown));
        Assert.That(CombatRules.ParseMove(""), Is.EqualTo(CombatClass.Unknown));
        Assert.That(CombatRules.ParseMove("Piedra"), Is.EqualTo(CombatClass.Unknown));
        Assert.That(CombatRules.ParseMove("7"), Is.EqualTo(CombatClass.Unknown));
    }
}