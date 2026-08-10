using System.Net;

namespace Acordanza.Api.Services;

// Centralizes the branded HTML layout and content for every transactional
// email, so the invite/reset-password/new-user flows share one look instead
// of each controller building its own ad hoc HTML string.
//
// Colors/radii below are manually mirrored from design-system/src/tokens.css
// (see /DESIGN.md) — email HTML can't consume CSS custom properties or import
// the design-system package (different runtime, and most email clients don't
// support var()), so these are copied as literal values. Keep them in sync by
// hand when tokens.css changes.
public static class EmailTemplates
{
    private const string AccentColor = "#3f3fe0"; // --color-primary
    private const string OnAccentColor = "#ffffff"; // --color-on-primary
    private const string CanvasColor = "#eef1f5"; // --color-canvas
    private const string SurfaceColor = "#f8f9fb"; // --color-surface
    private const string InkColor = "#181b22"; // --color-ink
    private const string InkDimColor = "#5b6270"; // --color-ink-dim
    private const string InkFaintColor = "#7d8492"; // --color-ink-faint
    private const string HairlineColor = "rgba(0,0,0,0.07)"; // --color-hairline
    private const string RadiusLg = "20px"; // --radius-lg
    private const string RadiusFull = "9999px"; // --radius-full
    private const string BodyFontStack = "'Inter',-apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif"; // --font-body
    private const string DisplayFontStack = "'Archivo',-apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif"; // --font-display

    private static readonly string ParagraphStyle = $"margin:0 0 16px; font-size:15px; line-height:1.5; color:{InkColor};";
    private static readonly string MutedStyle = $"margin:16px 0 0; font-size:13px; line-height:1.5; color:{InkDimColor};";

    public static (string Subject, string Html) ResetPassword(string resetLink, string frontendBaseUrl) => (
        "Restablece tu contraseña de Acordanza",
        Layout($"""
            <p style="{ParagraphStyle}">Has solicitado restablecer tu contraseña de Acordanza.</p>
            {Button("Elegir nueva contraseña", resetLink)}
            <p style="{MutedStyle}">Si tú no has solicitado esto, puedes ignorar este correo.</p>
            """, frontendBaseUrl));

    public static (string Subject, string Html) InviteUser(string inviteLink, string frontendBaseUrl) => (
        "Te han invitado a Acordanza",
        Layout($"""
            <p style="{ParagraphStyle}">Te han invitado a unirte a Acordanza.</p>
            {Button("Completar registro", inviteLink)}
            <p style="{MutedStyle}">Si no esperabas esta invitación, puedes ignorar este correo.</p>
            """, frontendBaseUrl));

    public static (string Subject, string Html) NewUserRegistered(string name, string email, string frontendBaseUrl) => (
        "Nuevo usuario registrado en Acordanza",
        Layout($"""
            <p style="{ParagraphStyle}">Un nuevo usuario ha completado su registro en Acordanza.</p>
            <p style="{ParagraphStyle}"><strong>{WebUtility.HtmlEncode(name)}</strong> ({WebUtility.HtmlEncode(email)})</p>
            """, frontendBaseUrl));

    public static (string Subject, string Html) AreaExpertApproved(string areaName, string frontendBaseUrl) => (
        "Tu solicitud de experto ha sido aprobada",
        Layout($"""
            <p style="{ParagraphStyle}">Tu solicitud para ser experto del sector <strong>{WebUtility.HtmlEncode(areaName)}</strong> ha sido aprobada.</p>
            <p style="{MutedStyle}">Ya puedes validar vías de este sector.</p>
            """, frontendBaseUrl));

    public static (string Subject, string Html) AreaExpertRejected(string areaName, string reason, string frontendBaseUrl) => (
        "Tu solicitud de experto ha sido rechazada",
        Layout($"""
            <p style="{ParagraphStyle}">Tu solicitud para ser experto del sector <strong>{WebUtility.HtmlEncode(areaName)}</strong> ha sido rechazada.</p>
            <p style="{ParagraphStyle}"><strong>Motivo:</strong> {WebUtility.HtmlEncode(reason)}</p>
            """, frontendBaseUrl));

    public static (string Subject, string Html) FirstAscensionistLinkApproved(string firstAscensionistName, string frontendBaseUrl) => (
        "Tu vínculo de aperturista ha sido aprobado",
        Layout($"""
            <p style="{ParagraphStyle}">Tu solicitud para vincular tu cuenta con el aperturista <strong>{WebUtility.HtmlEncode(firstAscensionistName)}</strong> ha sido aprobada.</p>
            <p style="{ParagraphStyle}">Ya puedes editar y validar las vías abiertas por este aperturista.</p>
            """, frontendBaseUrl));

    public static (string Subject, string Html) FirstAscensionistLinkRejected(string firstAscensionistName, string reason, string frontendBaseUrl) => (
        "Tu vínculo de aperturista ha sido rechazado",
        Layout($"""
            <p style="{ParagraphStyle}">Tu solicitud para vincular tu cuenta con el aperturista <strong>{WebUtility.HtmlEncode(firstAscensionistName)}</strong> ha sido rechazada.</p>
            <p style="{ParagraphStyle}"><strong>Motivo:</strong> {WebUtility.HtmlEncode(reason)}</p>
            """, frontendBaseUrl));

    public static (string Subject, string Html) ConfirmEmailChange(string confirmLink, string frontendBaseUrl) => (
        "Confirma tu nuevo correo de Acordanza",
        Layout($"""
            <p style="{ParagraphStyle}">Has pedido usar esta dirección para tu cuenta de Acordanza.</p>
            {Button("Confirmar correo electrónico", confirmLink)}
            <p style="{MutedStyle}">Si tú no has pedido esto, puedes ignorar este correo.</p>
            """, frontendBaseUrl));

    public static (string Subject, string Html) EmailChangeRequested(string newEmail, string frontendBaseUrl) => (
        "Se ha solicitado cambiar el correo de tu cuenta",
        Layout($"""
            <p style="{ParagraphStyle}">Se ha solicitado cambiar el correo de tu cuenta de Acordanza a <strong>{WebUtility.HtmlEncode(newEmail)}</strong>. El cambio no se aplicará hasta que se confirme desde esa dirección.</p>
            <p style="{MutedStyle}">Si no has sido tú, cambia tu contraseña ahora mismo.</p>
            """, frontendBaseUrl));

    private static string Button(string label, string url)
    {
        var safeUrl = WebUtility.HtmlEncode(url);
        return $"""
            <table role="presentation" cellpadding="0" cellspacing="0" style="margin:8px 0 24px;">
              <tr>
                <td style="border-radius:{RadiusFull}; background-color:{AccentColor};">
                  <a href="{safeUrl}" style="display:inline-block; padding:12px 28px; font-family:{BodyFontStack}; font-size:14px; font-weight:600; color:{OnAccentColor}; text-decoration:none; border-radius:{RadiusFull};">{WebUtility.HtmlEncode(label)}</a>
                </td>
              </tr>
            </table>
            """;
    }

    // The brand mark is a hosted PNG (frontend/public/brand-email.png, the same
    // tile mark as favicon.svg) referenced absolutely via Frontend:BaseUrl —
    // email clients strip inline SVG and most block data: URIs, so a fetched
    // image is the only portable option. The adjacent wordmark keeps the header
    // readable when images are blocked.
    private static string Layout(string bodyHtml, string frontendBaseUrl) => $"""
        <!DOCTYPE html>
        <html lang="es">
          <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <link rel="preconnect" href="https://fonts.googleapis.com" />
            <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;600&family=Archivo:wght@600&display=swap" rel="stylesheet" />
          </head>
          <body style="margin:0; padding:0; background-color:{CanvasColor}; font-family:{BodyFontStack};">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:{CanvasColor}; padding:32px 16px;">
              <tr>
                <td align="center">
                  <table role="presentation" width="480" cellpadding="0" cellspacing="0" style="max-width:480px; width:100%; background-color:{SurfaceColor}; border-radius:{RadiusLg}; overflow:hidden;">
                    <tr>
                      <td style="background-color:{AccentColor}; padding:24px 32px;">
                        <table role="presentation" cellpadding="0" cellspacing="0">
                          <tr>
                            <td style="padding-right:10px;"><img src="{WebUtility.HtmlEncode(frontendBaseUrl.TrimEnd('/'))}/brand-email.png" width="26" height="26" alt="" style="display:block; border:0;" /></td>
                            <td style="color:{OnAccentColor}; font-family:{DisplayFontStack}; font-weight:600; letter-spacing:0.12em; font-size:14px;">Acordanza</td>
                          </tr>
                        </table>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:32px;">
                        {bodyHtml}
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:20px 32px; background-color:{CanvasColor}; border-top:1px solid {HairlineColor};">
                        <p style="margin:0; font-size:12px; color:{InkFaintColor};">Acordanza &middot; catálogo de vías de escalada</p>
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
