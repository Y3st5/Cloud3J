# Módulo Combat — duelo de gremios por asaltos

Duelo por turnos entre dos gremios dentro de la semana del combate: cada jugador
manda una secuencia de 3 movimientos y el servidor resuelve los choques, reparte
el daño y declara el ganador. El combate **no se decide en el cliente**: la regla
de arquitectura del proyecto dice que el cliente pide, Cloud Code decide y el
cliente muestra el resultado.

## Las reglas

- Cada jugador manda **3 movimientos por asalto** (los enums viajan como texto:
  `Warrior`, `Mage`, `Assassin`).
- Depredación: el **Guerrero** vence al Asesino, el **Mago** al Guerrero y el
  **Asesino** al Mago. El choque igual es empate.
- El que gana un choque inflige **10 de daño**. Empiezan con **100 de vida**.
- Cuando los dos han jugado su secuencia, el servidor resuelve la ronda, reparte
  el daño y avanza a la siguiente.
- Quien llega a 0 de vida pierde el duelo.
- Si el rival no mueve en **5 minutos**, cualquiera de los dos puede reclamar la
  victoria. Es el servidor quien controla el reloj, nunca el cliente.

## El problema que resuelve (misma idea que TurnMatch)

La red pierde respuestas, no peticiones. Un reintento no debe contar el doble, y
dos peticiones simultáneas no deben pisarse. Por eso `CombatModule` usa la misma
pareja que TurnMatch:

| | Protege contra | Qué pasa sin él |
|---|---|---|
| **Clave de idempotencia** (`requestId`) | El reintento de un mismo cliente | El daño se aplica dos veces |
| **Write lock** de Cloud Save | Dos jugadores escribiendo a la vez | Un asalto pisa al otro |

Con la idempotencia hay un matiz extra: cuando una petición resolvió una ronda,
en el replay **no se puede devolver el estado actual** (ya avanzó) ni volver a
recalcular el daño (duplicaría). Se devuelve la **instantánea** que se guardó al
aplicar la petición: `ProcessedRequest` guarda HP, ronda, estado y ganador de
ese momento, y `CombatRules.SnapshotOf` la reconstruye.

El reparto de movimientos por ronda también es idempotente por construcción: si
un jugador reenvía su secuencia sin que haya resuelto la ronda, solo se
**sobrescribe su parte**; el duelo no avanza hasta que existan las dos.

## Dónde vive el estado

Cloud Save, como **custom data privado**, igual que TurnMatch:

- **Estado del duelo**: collection/items privados escritos con el `ServiceToken`
  del módulo. Ni siquiera los dos participantes pueden reescribirlo desde su
  cliente.
- **Índice del jugador** (`combat_index`, con su `PlayerId` como item): la lista
  de duelos en curso que el módulo devuelve en `GetActiveMatches`. Se escribe
  también con el `ServiceToken` del módulo; el cliente no la toca.

El `writeLock` que devuelve la lectura se reenvía al guardar. Si otra escritura
entró en medio, Cloud Save responde 409 y el módulo responde `Error` pidiendo
reintentar con la **misma** petición (que ya está anotada como procesada).

## El identificador del duelo

Se **deriva del `requestId`** con un hash, igual que en TurnMatch: si el
identificador fuera aleatorio, reintentar "crear duelo" dejaría al jugador con
dos. Derivándolo, el reintento cae sobre el mismo duelo y el módulo responde
`replayed`. Alfabeto sin `I`, `O`, `0` ni `1`.

## Endpoints

| Función | Parámetros | Respuesta | Qué hace |
|---|---|---|---|
| `CreateMatch` | `requestId` | `CombatResponse` | Crea el duelo y devuelve su `MatchId` |
| `JoinMatch` | `matchId`, `requestId` | `CombatResponse` | Entra el segundo jugador |
| `SubmitTurn` | `matchId`, `requestId`, `moves` (texto) | `CombatResponse` | Envía la secuencia; resuelve si hay dos |
| `ClaimVictory` | `matchId`, `requestId` | `CombatResponse` | Victoria por incomparecencia |
| `GetMatch` | `matchId` | `CombatMatch` | Consulta el estado de un duelo |
| `GetActiveMatches` | — | `List<CombatMatch>` | Duelos en curso del jugador |

`CombatResponse.Status`: `applied`, `replayed`, `TurnSubmitted`, `RoundResolved`,
`VictoryClaimed`, `Error`.

Los rechazos normales (ráfaga inválida, no te toca, duelo lleno, tiempo no agotado)
viajan como **excepción** dentro del módulo, que Cloud Code convierte en
`ScriptError`: son mensajes para el jugador. `Error` se reserva para el conflicto
de escritura, donde el cliente debe reintentar con la misma petición.

## Probarlo

```bash
dotnet test CloudCode.Modules/Combat/Combat.sln
```

19 pruebas sobre `CombatRules` (depredación, resolución de ronda, idempotencia y
parseo de movimientos). Corren sin servidor porque la lógica está separada de la
entrada/salida: `CombatRules` no sabe que existe Cloud Save.

## Demostrarlo en clase

1. Despliega el módulo desde la ventana de Deployment y abre la escena de combate
   (menú `Cloud2026 > Montar UI de combate (GAME)`).
2. Crea un duelo en un cliente y pasa el `MatchId` al otro para que se una.
3. Alternad asaltos de 3 movimientos. Cuando los dos han jugado, el servidor
   resuelve y la ronda avanza.
4. Para ver la idempotencia: fuerza una repetición del mismo `requestId` (desde
   el navegador de red) y compara con una petición nueva: la primera no aplica
   nada y devuelve la instantánea; la segunda sí aplica sobre el estado actual.

## Lo que este módulo no cubre

- **Creación simultánea del mismo identificador.** `CreateMatch` lee y escribe
  sin lock previo, igual que TurnMatch: improbable con 4 caracteres del alfabeto.
- **Limpieza de duelos.** Nada borra los duelos terminados.
- **Tiempo real.** La escena no sondea. Un asalto resuelto solo se ve cuando el
  jugador que mandó el primer movimiento vuelve a jugar o consulta el hub.
- **Economía.** El duelo no reparte recompensas ni toca saldo: no hay premio que
  valide contra Economy todavía. Cuando exista, irá en otro endpoint con su
  transacción autoritativa.