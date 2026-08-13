# Instalador de Reloj Checador

Genera un instalador de Windows (`RelojChecador-Setup-<version>.exe`) que instala la
aplicación WPF como ejecutable autocontenido (no requiere que el equipo destino tenga
.NET preinstalado).

## Requisitos (en Windows)

- .NET 10 SDK
- [Inno Setup 6.x](https://jrsoftware.org/isinfo.php) — instala `ISCC.exe`, el compilador
  de scripts `.iss`. No existe una versión de Inno Setup para macOS/Linux.

## Pasos

```powershell
# 1) Publicar el ejecutable autocontenido (win-x86 — 32 bits, requerido por
#    zkemkeeper.dll, ver third-party/zkteco-sdk/README.md — un solo archivo)
.\installer\publish.ps1

# 2) Compilar el instalador con Inno Setup
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\RelojChecador.iss
```

El instalador queda en `installer\output\RelojChecador-Setup-1.0.0.exe`.

## Antes de distribuir a producción

- **Razón social**: reemplazar `MyAppPublisher` en `RelojChecador.iss` por el nombre legal
  real del carwash (pendiente de confirmar — ver Fase 6/nómina).
- **Icono de la aplicación**: el proyecto WPF aún usa el icono por defecto de .NET; falta
  diseñar/proveer un `.ico` propio antes de la versión final.
- **Firma de código**: sin firmar, Windows SmartScreen mostrará una advertencia al primer
  usuario que lo ejecute. Firmar requiere un certificado de firma de código (de pago) y
  `signtool.exe` — no incluido en este script todavía.
- **Versión**: `MyAppVersion` en el `.iss` se actualiza manualmente por ahora; se puede
  automatizar más adelante para que tome la versión del ensamblado publicado.

## Qué NO borra el desinstalador (a propósito)

La base de datos SQLite local, los logs y la configuración viven en
`%LocalAppData%\RelojChecador` — el desinstalador **no** toca esa carpeta. Es información
del negocio (asistencia, empleados, sucursales), no un archivo temporal de la app.

## Estado

Este script **no se ha compilado ni probado todavía** — se escribió y se documentó desde
macOS, donde `ISCC.exe` no existe. La primera vez que se ejecute en Windows real hay que
revisarlo con calma (rutas, permisos de instalación en Archivos de Programa, etc.) antes
de confiar en él para distribución.
