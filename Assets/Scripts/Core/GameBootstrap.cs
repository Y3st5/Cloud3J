using System;
using UnityEngine;
using Cloud2026.Services;

namespace Cloud2026.Core
{
    /// <summary>
    /// Punto de entrada del juego. Gestiona el ciclo de vida de los servicios UGS
    /// y la persistencia de la sesión entre escenas.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameBootstrap : MonoBehaviour
    {
        public static GameBootstrap Instance { get; private set; }

        [Header("Servicios")]
        [SerializeField] public UGSAuthService authService;

        [Tooltip("Wrapper de Cloud Code. Necesita sesión iniciada para poder llamar al servidor.")]
        [SerializeField] public UGSCloudCodeService cloudCodeService;

        [Tooltip("Cliente del módulo TurnMatch: partidas por turnos con idempotencia.")]
        [SerializeField] public UGSTurnMatchService turnMatchService;

        [Tooltip("Wrapper de Cloud Save: guarda y carga el perfil del jugador. Necesita sesión iniciada.")]
        [SerializeField] public UGSCloudSaveService cloudSaveService;

        [Header("Configuración de Arranque")]
        [Tooltip("Si es true, no destruye este GameObject al cargar nuevas escenas.")]
        [SerializeField] private bool persistAcrossScenes = true;

        [Tooltip("Si es true, intenta realizar login anónimo automático tras inicializar.")]
        [SerializeField] private bool autoLoginAnonymous = false;

        public IAuthService AuthService => authService;

        public ICloudCodeService CloudCodeService => cloudCodeService;

        public ITurnMatchService TurnMatchService => turnMatchService;

        public ICloudSaveService CloudSaveService => cloudSaveService;

        public event Action OnServicesReady;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (persistAcrossScenes)
            {
                DontDestroyOnLoad(gameObject);
            }

            EnsureServicesAssigned();
        }

        private async void Start()
        {
            if (authService != null)
            {
                await authService.InitializeAsync();
                OnServicesReady?.Invoke();

                if (autoLoginAnonymous && !authService.IsSignedIn)
                {
                    await authService.SignInAnonymouslyAsync();
                }
            }
        }

        private void EnsureServicesAssigned()
        {
            authService = EnsureComponent(authService);
            cloudCodeService = EnsureComponent(cloudCodeService);
            turnMatchService = EnsureComponent(turnMatchService);
            cloudSaveService = EnsureComponent(cloudSaveService);
        }

        /// <summary>
        /// Devuelve el servicio ya asignado en el inspector; si falta, lo busca entre
        /// los hijos y, como último recurso, lo añade a este mismo GameObject.
        /// </summary>
        private T EnsureComponent<T>(T current) where T : Component
        {
            if (current != null)
            {
                return current;
            }

            var found = GetComponentInChildren<T>();
            return found != null ? found : gameObject.AddComponent<T>();
        }
    }
}
