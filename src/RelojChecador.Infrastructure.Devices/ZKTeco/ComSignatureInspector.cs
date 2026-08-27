using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace RelojChecador.Infrastructure.Devices.ZKTeco;

/// <summary>
/// Ayuda de diagnóstico, NO de operación normal: cuando una llamada a un método de
/// <c>zkemkeeper.dll</c> por enlace tardío (<c>dynamic</c>, ver el comentario de clase de
/// ZKTecoDeviceAdapter) falla con un mensaje genérico como "Error while invoking X" — típico
/// de .NET cuando el número/tipo de parámetros no coincide con lo que expone el COM real —,
/// esta clase le pregunta DIRECTO al objeto COM cuál es su firma real vía su
/// <c>ITypeInfo</c> (el mismo mecanismo que usan herramientas como OleView/tlbimp, sin
/// necesitar generar un ensamblado de interop). Se usa para diagnosticar en el momento en
/// vez de adivinar otra firma a ciegas — caso real: <c>GetUserTmpExStr</c>/
/// <c>SetUserTmpExStr</c> (ver DownloadUserTemplatesAsync/UploadUserTemplateAsync), la única
/// parte de este SDK nunca confirmada contra hardware real.
///
/// No requiere <c>net*-windows</c> ni un ensamblado de interop generado: <see cref="ITypeInfo"/>,
/// <see cref="FUNCDESC"/>, <see cref="ELEMDESC"/> y <see cref="TYPEATTR"/> ya vienen en el
/// BCL (<c>System.Runtime.InteropServices.ComTypes</c>) — lo único que hace falta declarar a
/// mano es <see cref="IDispatchMinimal"/> (el propio BCL no expone <c>IDispatch</c> como tipo
/// público), con SOLO los dos primeros métodos de su vtable real (<c>GetTypeInfoCount</c>,
/// <c>GetTypeInfo</c>) — es válido omitir el resto de la vtable (GetIDsOfNames/Invoke)
/// mientras se declaren en el mismo orden desde el principio, sin saltarse ninguno.
/// </summary>
internal static class ComSignatureInspector
{
    [ComImport, Guid("00020400-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDispatchMinimal
    {
        void GetTypeInfoCount(out int count);
        void GetTypeInfo(int iTInfo, int lcid, out ITypeInfo typeInfo);
    }

    /// <summary>Devuelve una descripción legible ("NombreMetodo(param0:VT_I4[in], param1:VT_BSTR[in,out], ...)")
    /// de la firma REAL del método <paramref name="methodName"/> tal cual la reporta el
    /// propio objeto COM, o null si el método no aparece en su ITypeInfo. Nunca lanza — un
    /// fallo al inspeccionar (objeto sin IDispatch, sin ITypeInfo, etc.) devuelve un texto
    /// explicándolo en vez de reventar, porque esto se llama desde un bloque catch que ya
    /// está manejando un error distinto.</summary>
    public static string Describe(object? comObject, string methodName)
    {
        if (comObject is not IDispatchMinimal dispatch)
        {
            return "(no se pudo inspeccionar: el objeto no expone IDispatch)";
        }

        try
        {
            dispatch.GetTypeInfo(0, 0, out var typeInfo);
            if (typeInfo is null)
            {
                return "(no se pudo inspeccionar: GetTypeInfo devolvió null)";
            }

            typeInfo.GetTypeAttr(out var typeAttrPtr);
            try
            {
                var typeAttr = Marshal.PtrToStructure<TYPEATTR>(typeAttrPtr);
                for (int i = 0; i < typeAttr.cFuncs; i++)
                {
                    typeInfo.GetFuncDesc(i, out var funcDescPtr);
                    try
                    {
                        var funcDesc = Marshal.PtrToStructure<FUNCDESC>(funcDescPtr);
                        var names = new string[funcDesc.cParams + 1];
                        typeInfo.GetNames(funcDesc.memid, names, names.Length, out _);

                        if (names.Length == 0 || !string.Equals(names[0], methodName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        return DescribeFunction(methodName, funcDesc, names);
                    }
                    finally
                    {
                        typeInfo.ReleaseFuncDesc(funcDescPtr);
                    }
                }
            }
            finally
            {
                typeInfo.ReleaseTypeAttr(typeAttrPtr);
            }

            return $"(\"{methodName}\" no aparece en el ITypeInfo del dispositivo — ¿nombre distinto en esta versión del SDK?)";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return $"(no se pudo inspeccionar: {ex.Message})";
        }
    }

    private static string DescribeFunction(string methodName, FUNCDESC funcDesc, string[] names)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(methodName).Append('(');

        for (int p = 0; p < funcDesc.cParams; p++)
        {
            var elemPtr = IntPtr.Add(funcDesc.lprgelemdescParam, p * Marshal.SizeOf<ELEMDESC>());
            var elem = Marshal.PtrToStructure<ELEMDESC>(elemPtr);
            var flags = (PARAMFLAG)elem.desc.paramdesc.wParamFlags;
            var paramName = p + 1 < names.Length && !string.IsNullOrEmpty(names[p + 1]) ? names[p + 1] : $"arg{p}";

            sb.Append(paramName).Append(':').Append(DescribeVarType(elem.tdesc.vt));
            if ((flags & PARAMFLAG.PARAMFLAG_FIN) != 0) sb.Append("[in]");
            if ((flags & PARAMFLAG.PARAMFLAG_FOUT) != 0) sb.Append("[out]");
            if (p < funcDesc.cParams - 1) sb.Append(", ");
        }

        sb.Append(") — ").Append(funcDesc.cParams).Append(" parámetro(s)");
        return sb.ToString();
    }

    /// <summary>vt trae el tipo base combinado con "modificadores" (VT_BYREF, VT_ARRAY) como
    /// bits aparte — VarEnum no está marcado [Flags] en el BCL, así que un cast directo no
    /// se lee bien (imprime el número crudo). Se separan a mano para que "por referencia"
    /// quede explícito, que es justo el dato que más importa para reproducir la llamada.</summary>
    private static string DescribeVarType(short vt)
    {
        const short VT_BYREF = 0x4000;
        const short VT_ARRAY = 0x2000;
        var baseType = (VarEnum)(vt & ~(VT_BYREF | VT_ARRAY));
        var suffix = (vt & VT_BYREF) != 0 ? " BYREF" : "";
        suffix += (vt & VT_ARRAY) != 0 ? " ARRAY" : "";
        return baseType + suffix;
    }
}
