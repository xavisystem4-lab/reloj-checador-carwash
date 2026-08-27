namespace RelojChecador.Application.Devices;

/// <summary>Una plantilla de huella tal cual vive en la memoria del dispositivo, para un PIN
/// y un dedo (<see cref="FingerIndex"/>, 0-9 — hasta 10 dedos por usuario según el SDK) en
/// concreto. <see cref="TemplateData"/> es un blob opaco propio del SDK del fabricante —
/// nunca se interpreta ni se valida aquí, solo se mueve tal cual de un PIN a otro (ver
/// DevicesViewModel.ChangeDeviceUserPinAsync, "mover" un enrolamiento a un PIN nuevo sin
/// tener que volver a poner el dedo físicamente en el lector). <see cref="Flag"/> es el
/// indicador que trae el propio SDK junto a la plantilla (validez/duress) — se guarda y se
/// reenvía tal cual, sin interpretarlo.</summary>
public sealed record FingerprintTemplateRecord(int FingerIndex, int Flag, string TemplateData);
