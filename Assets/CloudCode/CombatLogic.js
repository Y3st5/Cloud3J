/**
 * CombatLogic.js
 * Lógica autoritativa para "Duelo de Gremios".
 * Se debe subir como un módulo de Cloud Code en el Dashboard de Unity.
 */

const COMBAT_RULES = {
    'Warrior': 'Assassin', // Warrior beats Assassin
    'Mage': 'Warrior',     // Mage beats Warrior
    'Assassin': 'Mage'     // Assassin beats Mage
};

const DAMAGE_PER_HIT = 10;

async function submitTurn(args, context) {
    const { matchId, moves } = args;
    const playerId = context.playerId;

    if (!moves || moves.length !== 3) {
        throw new Error("Debes enviar exactamente 3 movimientos.");
    }

    // 1. Guardar los movimientos del jugador en Cloud Save (Estado de la partida)
    // El estado de la partida es un JSON en Cloud Save
    const matchData = await context.cloudSave.GetItem('match_' + matchId);
    const state = JSON.parse(matchData);

    state.turns[playerId] = {
        moves: moves,
        timestamp: Date.now()
    };

    await context.cloudSave.SetItem('match_' + matchId, JSON.stringify(state));

    // 2. Verificar si el rival ya movió
    const players = [state.player1Id, state.player2Id];
    const otherPlayer = players.find(id => id !== playerId);

    if (state.turns[otherPlayer]) {
        // ¡Ambos han movido! Resolvemos la ronda.
        return await resolveRound(matchId, state, context);
    }

    return { status: "TurnSubmitted", message: "Esperando al rival..." };
}

async function resolveRound(matchId, state, context) {
    const p1Id = state.player1Id;
    const p2Id = state.player2Id;
    const p1Moves = state.turns[p1Id].moves;
    const p2Moves = state.turns[p2Id].moves;

    const roundResult = {
        round: state.currentRound,
        collisions: [],
        p1Damage: 0,
        p2Damage: 0
    };

    for (let i = 0; i < 3; i++) {
        const m1 = p1Moves[i];
        const m2 = p2Moves[i];
        let result = "DRAW";

        if (COMBAT_RULES[m1] === m2) {
            result = "P1_WINS";
            roundResult.p2Damage += DAMAGE_PER_HIT;
        } else if (COMBAT_RULES[m2] === m1) {
            result = "P2_WINS";
            roundResult.p1Damage += DAMAGE_PER_HIT;
        }

        roundResult.collisions.push({ p1Move: m1, p2Move: m2, result: result });
    }

    // Actualizar HP
    state.player1HP -= roundResult.p1Damage;
    state.player2HP -= roundResult.p2Damage;
    state.currentRound++;

    // Limpiar turnos para la siguiente ronda
    state.turns = {};

    // Verificar victoria final
    if (state.player1HP <= 0) {
        state.winnerId = p2Id;
        state.status = "Resolved";
    } else if (state.player2HP <= 0) {
        state.winnerId = p1Id;
        state.status = "Resolved";
    }

    await context.cloudSave.SetItem('match_' + matchId, JSON.stringify(state));

    return {
        status: "RoundResolved",
        result: roundResult,
        newState: state
    };
}

module.exports = { submitTurn };
