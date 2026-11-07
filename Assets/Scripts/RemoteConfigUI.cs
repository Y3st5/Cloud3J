using UnityEngine;
using TMPro;
using Cloud2026.Services; // Asegúrate de que este namespace coincida con el de tu servicio

public class RemoteConfigUI : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private UGSRemoteConfigService remoteConfigService;
    [SerializeField] private TextMeshProUGUI textoOrigen;
    [SerializeField] private TextMeshProUGUI textoEstadisticas;

    [Header("Configuración")]
    [SerializeField] private string idHeroeAMostrar = "guerrero";

    void Start()
    {
        ActualizarPantalla();
    }

    public void ActualizarPantalla()
    {
        if (remoteConfigService == null) return;

        // Muestra el origen en la esquina (Requisito del paso 6)
        textoOrigen.text = $"Balance desde: {remoteConfigService.OrigenDeDatos}";

        // Muestra los Valores Base (aquí se reflejará tu 99)
        string textoFinal = $"--- VALORES BASE ---\n" +
                            $"Vida Base: {remoteConfigService.VidaBase}\n" +
                            $"Ataque Base: {remoteConfigService.AtaqueBase}\n\n";

        // Muestra también los datos del JSON
        var heroe = remoteConfigService.BuscarHeroe(idHeroeAMostrar);
        if (heroe != null)
        {
            textoFinal += $"--- TABLA JSON ---\n" +
                          $"Unidad: {heroe.id.ToUpper()}\n" +
                          $"Vida: {heroe.vida} | Ataque: {heroe.ataque} | Def: {heroe.defensa}";
        }

        textoEstadisticas.text = textoFinal;
    }
    public async void RefrescarDesdeLaNube()
    {
        Debug.Log("Refrescando datos en caliente...");
        await remoteConfigService.InicializarYDescargar();
        ActualizarPantalla();
    }
}