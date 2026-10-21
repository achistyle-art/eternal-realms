using UnityEngine;

namespace EternalRealms.Client
{
    /// <summary>Kontoschutz (Zwei-Faktor-Anmeldung, Passwort ändern) und Hinweis während einer Wiederverbindung (Kapitel 40/41).</summary>
    public sealed partial class GameUI
    {
        private bool _showAccount;
        private string _accCode = "", _accPassword = "", _accNewPassword = "", _accNewPassword2 = "";

        private void ToggleAccountWindow()
        {
            _showAccount = !_showAccount;
            if (_showAccount) Client.RequestAccountSecurity();
        }

        private void DrawAccountWindow()
        {
            if (!_showAccount) return;
            var r = Box(new Rect(ScreenW / 2 + 270, 80, 380, 520));
            GUILayout.BeginArea(r, GUI.skin.box);
            GUILayout.Label("Kontoschutz", _title);
            var sec = Client.AccountSecurity;
            bool enabled = sec?.TwoFactorEnabled == true;
            GUILayout.Label("Zwei-Faktor-Anmeldung: " + (enabled ? "<color=lime>aktiv</color>" : "<color=orange>aus</color>"), _wrap);
            if (!enabled)
            {
                if (sec != null && sec.PendingSecret.Length > 0)
                {
                    GUILayout.Label("Tragt dieses Geheimnis in Eure Authenticator-App ein (zeitbasiert, 6 Ziffern):", _wrap);
                    GUILayout.TextField(sec.PendingSecret);
                    GUILayout.TextField(sec.PendingUri);
                    GUILayout.Label("Angezeigter Code");
                    _accCode = GUILayout.TextField(_accCode, 8);
                    if (GUILayout.Button("Bestätigen und aktivieren")) { Client.ConfirmTwoFactor(_accCode.Trim()); _accCode = ""; }
                }
                else if (GUILayout.Button("Zwei-Faktor-Anmeldung einrichten")) Client.BeginTwoFactor();
            }
            GUILayout.Space(8);
            GUILayout.Label("Aktuelles Passwort");
            _accPassword = GUILayout.PasswordField(_accPassword, '•', 128);
            if (enabled)
            {
                GUILayout.Label("Code der Authenticator-App");
                _accCode = GUILayout.TextField(_accCode, 8);
            }
            GUILayout.Label("Neues Passwort (mind. 8 Zeichen, Buchstaben und Ziffern)");
            _accNewPassword = GUILayout.PasswordField(_accNewPassword, '•', 128);
            _accNewPassword2 = GUILayout.PasswordField(_accNewPassword2, '•', 128);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Passwort ändern"))
            {
                if (_accNewPassword != _accNewPassword2) _status = "Die neuen Passwörter stimmen nicht überein.";
                else { Client.ChangePassword(_accPassword, _accNewPassword, _accCode.Trim()); ClearAccountFields(); }
            }
            if (enabled && GUILayout.Button("2FA abschalten")) { Client.DisableTwoFactor(_accPassword, _accCode.Trim()); ClearAccountFields(); }
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            if (sec != null && sec.Message.Length > 0) GUILayout.Label(sec.Message, _wrap);
            if (GUILayout.Button("Schließen")) _showAccount = false;
            GUILayout.EndArea();
        }

        private void ClearAccountFields() { _accCode = _accPassword = _accNewPassword = _accNewPassword2 = ""; }

        /// <summary>Verbindung abgerissen: der Client verbindet sich selbst neu, der Charakter wartet so lange in der Welt.</summary>
        private void DrawReconnectOverlay()
        {
            if (!Client.Reconnecting) return;
            var r = Box(new Rect(ScreenW / 2 - 200, ScreenH / 2 - 60, 400, 120));
            GUILayout.BeginArea(r, GUI.skin.box);
            GUILayout.Label("Verbindung verloren", _title);
            GUILayout.Label($"Neuer Versuch {Mathf.Max(1, Client.ReconnectAttempt)} von {Shared.Client.GameClient.MaxReconnectAttempts} … Euer Charakter bleibt so lange in der Welt.", _wrap);
            if (GUILayout.Button("Abbrechen")) Client.Disconnect();
            GUILayout.EndArea();
        }
    }
}
