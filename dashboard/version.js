// Versión del sitio — la MISMA del sistema (Directory.Build.props / instalador de la PC), para que
// quien lo use sepa qué versión está viendo, igual que el pie "v1.xx.x" de la app de escritorio.
// Se sube junto con cada release: tests/dashboard/version.test.mjs falla si no coincide con
// Directory.Build.props.
export const APP_VERSION = '1.68.0';
