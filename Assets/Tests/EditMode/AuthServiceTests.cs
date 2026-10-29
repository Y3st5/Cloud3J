using System;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Cloud2026.Services;

namespace Cloud2026.Tests
{
    /// <summary>
    /// Pruebas EditMode para validar los contratos de arquitectura de autenticación y manejo de estados.
    /// </summary>
    public class AuthServiceTests
    {
        private class FakeAuthService : IAuthService
        {
            public event Action<string> OnSignedIn;
            public event Action OnSignedOut;
            public event Action<string> OnSignInFailed;
            public event Action<string> OnAccountLinked;

            public bool IsInitialized { get; set; }
            public bool IsSignedIn { get; set; }
            public string PlayerId { get; set; }
            public string PlayerName { get; set; }
            public string Username { get; set; } = string.Empty;
            public bool IsUnityAccountLinked { get; set; }
            public bool IsAnonymous => IsSignedIn && string.IsNullOrEmpty(Username) && !IsUnityAccountLinked;

            public bool ShouldFail { get; set; }
            public string SimulatedPlayerId { get; set; } = "test-player-123456";

            public Task InitializeAsync()
            {
                IsInitialized = true;
                return Task.CompletedTask;
            }

            public Task<bool> SignInAnonymouslyAsync()
            {
                if (ShouldFail)
                {
                    OnSignInFailed?.Invoke("Error de prueba simulado");
                    return Task.FromResult(false);
                }

                IsSignedIn = true;
                PlayerId = SimulatedPlayerId;
                OnSignedIn?.Invoke(PlayerId);
                return Task.FromResult(true);
            }

            public void SignOut(bool clearCredentials = false)
            {
                IsSignedIn = false;
                PlayerId = string.Empty;
                OnSignedOut?.Invoke();
            }

            public Task<bool> SignUpWithUsernamePasswordAsync(string username, string password)
            {
                if (ShouldFail)
                {
                    OnSignInFailed?.Invoke("Registro rechazado");
                    return Task.FromResult(false);
                }

                IsSignedIn = true;
                PlayerId = SimulatedPlayerId;
                Username = username;
                OnSignedIn?.Invoke(PlayerId);
                return Task.FromResult(true);
            }

            public Task<bool> SignInWithUsernamePasswordAsync(string username, string password)
            {
                if (ShouldFail)
                {
                    OnSignInFailed?.Invoke("Credenciales incorrectas");
                    return Task.FromResult(false);
                }

                IsSignedIn = true;
                PlayerId = SimulatedPlayerId;
                Username = username;
                OnSignedIn?.Invoke(PlayerId);
                return Task.FromResult(true);
            }

            public Task<bool> LinkUsernamePasswordAsync(string username, string password)
            {
                if (!IsSignedIn)
                {
                    OnSignInFailed?.Invoke("No hay sesión que vincular");
                    return Task.FromResult(false);
                }

                if (ShouldFail)
                {
                    OnSignInFailed?.Invoke("Ese usuario ya existe");
                    return Task.FromResult(false);
                }

                Username = username;
                OnAccountLinked?.Invoke(username);
                return Task.FromResult(true);
            }

            public Task<bool> SignInWithUnityAsync()
            {
                if (ShouldFail)
                {
                    OnSignInFailed?.Invoke("Login con Unity rechazado");
                    return Task.FromResult(false);
                }

                IsSignedIn = true;
                PlayerId = SimulatedPlayerId;
                IsUnityAccountLinked = true;
                OnSignedIn?.Invoke(PlayerId);
                return Task.FromResult(true);
            }

            public Task<bool> LinkWithUnityAsync()
            {
                if (!IsSignedIn)
                {
                    OnSignInFailed?.Invoke("No hay sesión que vincular");
                    return Task.FromResult(false);
                }

                if (ShouldFail)
                {
                    OnSignInFailed?.Invoke("Esa cuenta de Unity ya está vinculada a otro jugador");
                    return Task.FromResult(false);
                }

                IsUnityAccountLinked = true;
                OnAccountLinked?.Invoke("tu cuenta de Unity");
                return Task.FromResult(true);
            }
        }

        [Test]
        public async Task FakeAuthService_SignInAnonymously_RaisesSignedInEventAndSetsState()
        {
            var auth = new FakeAuthService();
            string receivedPlayerId = null;
            auth.OnSignedIn += id => receivedPlayerId = id;

            bool result = await auth.SignInAnonymouslyAsync();

            Assert.IsTrue(result);
            Assert.IsTrue(auth.IsSignedIn);
            Assert.AreEqual("test-player-123456", auth.PlayerId);
            Assert.AreEqual("test-player-123456", receivedPlayerId);
        }

        [Test]
        public async Task FakeAuthService_SignInFailure_RaisesFailedEvent()
        {
            var auth = new FakeAuthService { ShouldFail = true };
            string errorMessage = null;
            auth.OnSignInFailed += err => errorMessage = err;

            bool result = await auth.SignInAnonymouslyAsync();

            Assert.IsFalse(result);
            Assert.IsFalse(auth.IsSignedIn);
            Assert.IsNotNull(errorMessage);
        }

        [Test]
        public async Task FakeAuthService_SignOut_ClearsPlayerStateAndRaisesEvent()
        {
            var auth = new FakeAuthService();
            bool signedOutFired = false;
            auth.OnSignedOut += () => signedOutFired = true;

            await auth.SignInAnonymouslyAsync();
            Assert.IsTrue(auth.IsSignedIn);

            auth.SignOut();
            Assert.IsFalse(auth.IsSignedIn);
            Assert.IsEmpty(auth.PlayerId);
            Assert.IsTrue(signedOutFired);
        }

        [Test]
        public async Task FakeAuthService_AnonymousSession_IsReportedAsAnonymous()
        {
            var auth = new FakeAuthService();
            await auth.SignInAnonymouslyAsync();

            Assert.IsTrue(auth.IsAnonymous, "Una sesión sin credenciales debe reportarse como anónima.");
            Assert.IsEmpty(auth.Username);
        }

        [Test]
        public async Task FakeAuthService_Link_KeepsPlayerIdAndClearsAnonymousFlag()
        {
            var auth = new FakeAuthService();
            await auth.SignInAnonymouslyAsync();
            string playerIdBeforeLink = auth.PlayerId;

            string linkedUsername = null;
            auth.OnAccountLinked += name => linkedUsername = name;

            bool result = await auth.LinkUsernamePasswordAsync("jugador01", "Passw0rd!");

            Assert.IsTrue(result);
            Assert.AreEqual("jugador01", linkedUsername);
            Assert.AreEqual("jugador01", auth.Username);
            Assert.IsFalse(auth.IsAnonymous, "Tras vincular, la sesión deja de ser anónima.");
            Assert.AreEqual(playerIdBeforeLink, auth.PlayerId,
                "Vincular credenciales no debe cambiar el PlayerId: el progreso se conserva.");
        }

        [Test]
        public async Task FakeAuthService_LinkWithoutSession_Fails()
        {
            var auth = new FakeAuthService();
            string error = null;
            auth.OnSignInFailed += err => error = err;

            bool result = await auth.LinkUsernamePasswordAsync("jugador01", "Passw0rd!");

            Assert.IsFalse(result);
            Assert.IsNotNull(error);
        }

        [Test]
        public async Task FakeAuthService_SignUp_LeavesSessionSignedInWithUsername()
        {
            var auth = new FakeAuthService();
            bool result = await auth.SignUpWithUsernamePasswordAsync("jugador02", "Passw0rd!");

            Assert.IsTrue(result);
            Assert.IsTrue(auth.IsSignedIn);
            Assert.AreEqual("jugador02", auth.Username);
            Assert.IsFalse(auth.IsAnonymous);
        }

        [Test]
        public async Task FakeAuthService_SignInWithUnity_RaisesSignedInEventAndSetsState()
        {
            var auth = new FakeAuthService();
            string receivedPlayerId = null;
            auth.OnSignedIn += id => receivedPlayerId = id;

            bool result = await auth.SignInWithUnityAsync();

            Assert.IsTrue(result);
            Assert.IsTrue(auth.IsSignedIn);
            Assert.AreEqual("test-player-123456", receivedPlayerId);
            Assert.IsTrue(auth.IsUnityAccountLinked);
            Assert.IsFalse(auth.IsAnonymous,
                "Entrar solo con una cuenta de Unity (sin usuario/contraseña) no debe verse como invitado.");
        }

        [Test]
        public async Task FakeAuthService_LinkWithUnity_KeepsPlayerIdAndRaisesAccountLinked()
        {
            var auth = new FakeAuthService();
            await auth.SignInAnonymouslyAsync();
            string playerIdBeforeLink = auth.PlayerId;

            string linkedLabel = null;
            auth.OnAccountLinked += label => linkedLabel = label;

            bool result = await auth.LinkWithUnityAsync();

            Assert.IsTrue(result);
            Assert.IsNotNull(linkedLabel);
            Assert.IsTrue(auth.IsUnityAccountLinked);
            Assert.IsFalse(auth.IsAnonymous, "Tras vincular una cuenta de Unity, la sesión deja de ser anónima.");
            Assert.AreEqual(playerIdBeforeLink, auth.PlayerId,
                "Vincular una cuenta de Unity no debe cambiar el PlayerId: el progreso se conserva.");
        }

        [Test]
        public async Task FakeAuthService_LinkWithUnityWithoutSession_Fails()
        {
            var auth = new FakeAuthService();
            string error = null;
            auth.OnSignInFailed += err => error = err;

            bool result = await auth.LinkWithUnityAsync();

            Assert.IsFalse(result);
            Assert.IsNotNull(error);
        }
    }
}
