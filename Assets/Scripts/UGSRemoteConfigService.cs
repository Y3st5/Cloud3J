using System;
using System.Threading.Tasks;
using Unity.Services.CloudCode;
using Unity.Services.RemoteConfig;
using UnityEngine;

namespace Cloud2026.Services
{
    // Clases para parsear el JSON de la tabla de héroes
    [Serializable]
    public class HeroeStats
    {
        public string id;
        public int vida;
        public int ataque;
        public int defensa;
        public int velocidad;
    }

    [Serializable]
    public class TablaHeroes
    {
        public HeroeStats[] heroes;
    }

    // Atributos para la segmentación del paso 8
    public struct AtributosApp
    {
        public string canal;
    }

    public struct AtributosUsuario { }

    public class UGSRemoteConfigService : MonoBehaviour
    {
        // Variables para guardar las estadísticas descargadas
        public int VidaBase { get; private set; }
        public int AtaqueBase { get; private set; }
        public float MultiplicadorDificultad { get; private set; }
        public TablaHeroes TablaDeHeroes { get; private set; }
        public bool EstaDescargadoDeLaNube { get; private set; }
        public string OrigenDeDatos { get; private set; }

        public async Task InicializarYDescargar()
        {
            RuntimeConfig config;
            bool deLaNube;
            bool hayCache = false;

            try
            {
                // Intenta descargar de la nube (Paso 4)
                config = await RemoteConfigService.Instance.FetchConfigsAsync(new AtributosUsuario(), new AtributosApp { canal = "beta" });
                deLaNube = true;
                OrigenDeDatos = "Nube";
                Debug.Log("[RemoteConfig] Configuracion descargada de la nube.");
                Debug.Log($"Entorno conectado: {config.environmentId}");
            }
            catch (Exception e)
            {
                // Si falla (por ejemplo, sin internet), usa lo que quedó en memoria (Paso 5)
                Debug.LogWarning($"[RemoteConfig] La nube no respondió: {e.Message}");
                deLaNube = false;
                config = RemoteConfigService.Instance.appConfig;
                hayCache = config.GetKeys().Length > 0;
                OrigenDeDatos = hayCache ? "Caché" : "Valores por defecto";
                Debug.Log(hayCache ? "[RemoteConfig] Usando cache." : "[RemoteConfig] Sin cache, usando valores por defecto.");
            }

            EstaDescargadoDeLaNube = deLaNube;

            // Extraer las variables (con valores por defecto)
            VidaBase = config.GetInt("vida_base", 100);
            AtaqueBase = config.GetInt("ataque_base", 10);
            MultiplicadorDificultad = config.GetFloat("multiplicador_dificultad", 1f);

            // Parsear el JSON
            if (config.HasKey("tabla_heroes"))
            {
                TablaDeHeroes = JsonUtility.FromJson<TablaHeroes>(config.GetJson("tabla_heroes", "{}"));
                Debug.Log($"[RemoteConfig] Tabla de heroes cargada. Encontrados: {TablaDeHeroes?.heroes?.Length}");
            }

            Debug.Log($"[RemoteConfig] Valores actuales - Vida: {VidaBase}, Ataque: {AtaqueBase}");
        }

        // Método para que las unidades pidan sus estadísticas (Paso 6)
        public HeroeStats BuscarHeroe(string idHeroe)
        {
            if (TablaDeHeroes == null || TablaDeHeroes.heroes == null) return null;

            foreach (var heroe in TablaDeHeroes.heroes)
            {
                if (heroe.id == idHeroe) return heroe;
            }
            return null;
        }
    }
}
