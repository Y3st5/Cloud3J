using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;
using Unity.Services.CloudSave.Model;

namespace Combat;

/// <summary>
/// El estado del duelo, más el write lock con el que se leyó.
///
/// El write lock es el control de concurrencia de Cloud Save: al guardar se
/// envía el que traía la lectura y, si otra escritura se coló en medio, el
/// servidor rechaza la nuestra en vez de pisarla en silencio.
/// </summary>
public record StoredCombat(CombatMatch State, string? WriteLock);

/// <summary>
/// Índice de duelos en los que participa el jugador. Se guarda bajo la custom
/// data privada del propio jugador (con su AccessToken por eso solo Cloud Code
/// y él la tocan) para poder listar "tus partidas activas" en el hub.
/// </summary>
public class CombatIndex
{
    /// <summary>
    /// La clave en disco es "match_ids" (snake_case): la misma que busca la
    /// lectura. Newtonsoft nombraría la propiedad "MatchIds" por defecto y el
    /// índice se escribiría con una clave y se leería con otra.
    /// </summary>
    [Newtonsoft.Json.JsonProperty("match_ids")]
    public List<string> MatchIds { get; set; } = new();
}

/// <summary>
/// Lectura y escritura del estado del duelo en Cloud Save.
///
/// Usa custom data *privado*: los datos cuelgan de un identificador propio
/// (combat_matches / el MatchId), no de un jugador, lo que permite que dos
/// personas compartan el mismo estado sin pisarse desde sus clientes. El estado
/// autoritativo se escribe con el ServiceToken del módulo: si usara el
/// AccessToken del jugador, el dato no sería privado.
/// </summary>
public class CombatRepository
{
    private const string MatchItemKey = "state";

    private const string IndexItemKey = "match_ids";

    private readonly IGameApiClient m_ApiClient;

    public CombatRepository(IGameApiClient apiClient)
    {
        m_ApiClient = apiClient;
    }

    /// <summary>Devuelve null si no existe ningún duelo con ese identificador.</summary>
    public async Task<StoredCombat?> LoadAsync(IExecutionContext context, string matchId)
    {
        try
        {
            var response = await m_ApiClient.CloudSaveData.GetPrivateCustomItemsAsync(
                context,
                context.ServiceToken,
                context.ProjectId,
                matchId,
                new List<string> { MatchItemKey },
                null!, // el SDK admite null aquí (paginación), pero no lo tiene anotado
                default);

            var item = response.Data?.Results?.FirstOrDefault(i => i.Key == MatchItemKey);
            if (item?.Value == null)
            {
                return null;
            }

            var state = ToCombatMatch(item.Value);
            return state == null ? null : new StoredCombat(state, item.WriteLock);
        }
        catch (ApiException e) when (StatusOf(e) == HttpStatusCode.NotFound)
        {
            // Todavía no ha escrito nadie con ese identificador: no es un fallo.
            return null;
        }
    }

    /// <summary>
    /// Guarda el estado. Pasa <paramref name="writeLock"/> tal cual vino de la
    /// lectura; si otro proceso escribió entre medias, esto lanza un conflicto.
    /// </summary>
    /// <returns>true si se guardó, false si otra escritura ganó la carrera.</returns>
    public async Task<bool> TrySaveAsync(IExecutionContext context, CombatMatch state, string? writeLock)
    {
        // writeLock nulo = escritura incondicional; es lo correcto al crear el duelo.
        var body = new SetItemBody(MatchItemKey, JObject.FromObject(state), writeLock!);

        try
        {
            await m_ApiClient.CloudSaveData.SetPrivateCustomItemAsync(
                context,
                context.ServiceToken,
                context.ProjectId,
                state.MatchId,
                body,
                default);

            return true;
        }
        catch (ApiException e) when (StatusOf(e) == HttpStatusCode.Conflict)
        {
            return false;
        }
    }

    // --- Índice del jugador ------------------------------------------------

    public async Task<CombatIndex> LoadIndexAsync(IExecutionContext context)
    {
        try
        {
            var response = await m_ApiClient.CloudSaveData.GetPrivateCustomItemsAsync(
                context,
                context.ServiceToken,
                context.ProjectId,
                context.PlayerId!,
                new List<string> { IndexItemKey },
                null!,
                default);

            var item = response.Data?.Results?.FirstOrDefault(i => i.Key == IndexItemKey);
            var json = item?.Value as JObject;
            var ids = json?[IndexItemKey]?.ToObject<List<string>>();
            return ids == null ? new CombatIndex() : new CombatIndex { MatchIds = ids };
        }
        catch (ApiException e) when (StatusOf(e) == HttpStatusCode.NotFound)
        {
            return new CombatIndex();
        }
    }

    /// <summary>
    /// Apunta el duelo en el índice del jugador. Los conflictos de escritura se
    /// resuelven releendo y reintentando: perder un duelo en la lista solo es un
    /// susto estético, así que no merece tumbar la operación que creó el duelo.
    /// </summary>
    public async Task AddToIndexAsync(IExecutionContext context, string matchId)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var index = await LoadIndexAsync(context);
            if (index.MatchIds.Contains(matchId))
            {
                return;
            }

            index.MatchIds.Add(matchId);

            var body = new SetItemBody(IndexItemKey, JObject.FromObject(index), null!);

            try
            {
                await m_ApiClient.CloudSaveData.SetPrivateCustomItemAsync(
                    context,
                    context.ServiceToken,
                    context.ProjectId,
                    context.PlayerId!,
                    body,
                    default);

                return;
            }
            catch (ApiException e) when (StatusOf(e) == HttpStatusCode.Conflict)
            {
                // Otra escritura sobre el índice ganó la carrera: releemos.
            }
        }
    }

    private static CombatMatch? ToCombatMatch(object value)
    {
        return value switch
        {
            JObject json => json.ToObject<CombatMatch>(),
            string text when text.Length > 0 => JObject.Parse(text).ToObject<CombatMatch>(),
            _ => JObject.FromObject(value).ToObject<CombatMatch>()
        };
    }

    private static HttpStatusCode? StatusOf(ApiException exception)
    {
        return exception.Response?.StatusCode;
    }
}