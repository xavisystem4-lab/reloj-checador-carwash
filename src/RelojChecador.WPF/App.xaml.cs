using System.Configuration;
using System.Data;
using System.Windows;

namespace RelojChecador.WPF;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
/// <remarks>
/// Se usa "System.Windows.Application" totalmente calificado a propósito: el proyecto
/// "RelojChecador.Application" (capa de casos de uso) comparte el espacio de nombres raíz
/// "RelojChecador" con este proyecto WPF, así que el nombre "Application" sin calificar
/// resuelve al namespace RelojChecador.Application en vez de al tipo de WPF. Un using-alias
/// no basta aquí porque los usings de un namespace de archivo (file-scoped) se evalúan
/// después que los miembros del namespace ancestro "RelojChecador" — de ahí la ambigüedad.
/// </remarks>
public partial class App : System.Windows.Application
{
}

