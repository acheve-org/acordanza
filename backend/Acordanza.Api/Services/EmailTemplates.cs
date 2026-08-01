using System.Net;

namespace Acordanza.Api.Services;

// Centralizes the branded HTML layout and content for every transactional
// email, so the invite/reset-password/new-user flows share one look instead
// of each controller building its own ad hoc HTML string.
public static class EmailTemplates
{
    private const string AccentColor = "#494fdf";
    private const string ParagraphStyle = "margin:0 0 16px; font-size:15px; line-height:1.5; color:#18181b;";
    private const string MutedStyle = "margin:16px 0 0; font-size:13px; line-height:1.5; color:#71717a;";

    public static (string Subject, string Html) ResetPassword(string resetLink) => (
        "Restablece tu contraseña de Acordanza",
        Layout($"""
            <p style="{ParagraphStyle}">Has solicitado restablecer tu contraseña de Acordanza.</p>
            {Button("Elegir nueva contraseña", resetLink)}
            <p style="{MutedStyle}">Si tú no has solicitado esto, puedes ignorar este correo.</p>
            """));

    public static (string Subject, string Html) InviteUser(string inviteLink) => (
        "Te han invitado a Acordanza",
        Layout($"""
            <p style="{ParagraphStyle}">Te han invitado a unirte a Acordanza.</p>
            {Button("Completar registro", inviteLink)}
            <p style="{MutedStyle}">Si no esperabas esta invitación, puedes ignorar este correo.</p>
            """));

    public static (string Subject, string Html) NewUserRegistered(string name, string email) => (
        "Nuevo usuario registrado en Acordanza",
        Layout($"""
            <p style="{ParagraphStyle}">Un nuevo usuario ha completado su registro en Acordanza.</p>
            <p style="{ParagraphStyle}"><strong>{WebUtility.HtmlEncode(name)}</strong> ({WebUtility.HtmlEncode(email)})</p>
            """));

    public static (string Subject, string Html) AreaExpertApproved(string areaName) => (
        "Tu solicitud de experto ha sido aprobada",
        Layout($"""
            <p style="{ParagraphStyle}">Tu solicitud para ser experto del sector <strong>{WebUtility.HtmlEncode(areaName)}</strong> ha sido aprobada.</p>
            <p style="{MutedStyle}">Ya puedes validar vías de este sector.</p>
            """));

    public static (string Subject, string Html) AreaExpertRejected(string areaName) => (
        "Tu solicitud de experto ha sido rechazada",
        Layout($"""
            <p style="{ParagraphStyle}">Tu solicitud para ser experto del sector <strong>{WebUtility.HtmlEncode(areaName)}</strong> ha sido rechazada.</p>
            """));

    public static (string Subject, string Html) FirstAscensionistLinkApproved(string firstAscensionistName) => (
        "Tu vínculo de aperturista ha sido aprobado",
        Layout($"""
            <p style="{ParagraphStyle}">Tu solicitud para vincular tu cuenta con el aperturista <strong>{WebUtility.HtmlEncode(firstAscensionistName)}</strong> ha sido aprobada.</p>
            <p style="{ParagraphStyle}">Ya puedes editar y validar las vías abiertas por este aperturista.</p>
            """));

    public static (string Subject, string Html) FirstAscensionistLinkRejected(string firstAscensionistName, string reason) => (
        "Tu vínculo de aperturista ha sido rechazado",
        Layout($"""
            <p style="{ParagraphStyle}">Tu solicitud para vincular tu cuenta con el aperturista <strong>{WebUtility.HtmlEncode(firstAscensionistName)}</strong> ha sido rechazada.</p>
            <p style="{ParagraphStyle}"><strong>Motivo:</strong> {WebUtility.HtmlEncode(reason)}</p>
            """));

    private static string Button(string label, string url)
    {
        var safeUrl = WebUtility.HtmlEncode(url);
        return $"""
            <table role="presentation" cellpadding="0" cellspacing="0" style="margin:8px 0 24px;">
              <tr>
                <td style="border-radius:9999px; background-color:#000000;">
                  <a href="{safeUrl}" style="display:inline-block; padding:12px 28px; font-family:'Inter',-apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif; font-size:14px; font-weight:600; color:#ffffff; text-decoration:none; border-radius:9999px;">{WebUtility.HtmlEncode(label)}</a>
                </td>
              </tr>
            </table>
            """;
    }

    private static string Layout(string bodyHtml) => $"""
        <!DOCTYPE html>
        <html lang="es">
          <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
          </head>
          <body style="margin:0; padding:0; background-color:#f4f4f5; font-family:'Inter',-apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#f4f4f5; padding:32px 16px;">
              <tr>
                <td align="center">
                  <table role="presentation" width="480" cellpadding="0" cellspacing="0" style="max-width:480px; width:100%; background-color:#ffffff; border-radius:20px; overflow:hidden;">
                    <tr>
                      <td style="background-color:#000000; padding:24px 32px;">
                        <span style="color:{AccentColor}; font-size:18px; vertical-align:middle;">&#9670;</span>
                        <span style="color:#ffffff; font-family:'Inter',-apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif; font-weight:600; letter-spacing:0.12em; font-size:14px; vertical-align:middle;">Acordanza</span>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:32px;">
                        {bodyHtml}
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:20px 32px; background-color:#f4f4f5; border-top:1px solid #e5e5e5;">
                        <p style="margin:0; font-size:12px; color:#8a8a8e;">Acordanza &middot; catálogo de vías de escalada</p>
                      </td>
                    </tr>
                  </table>
                </td>
              </tr>
            </table>
          </body>
        </html>
        """;
}
