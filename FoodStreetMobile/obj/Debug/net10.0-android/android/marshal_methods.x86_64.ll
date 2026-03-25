; ModuleID = 'marshal_methods.x86_64.ll'
source_filename = "marshal_methods.x86_64.ll"
target datalayout = "e-m:e-p270:32:32-p271:32:32-p272:64:64-i64:64-f80:128-n8:16:32:64-S128"
target triple = "x86_64-unknown-linux-android21"

%struct.MarshalMethodName = type {
	i64, ; uint64_t id
	ptr ; char* name
}

%struct.MarshalMethodsManagedClass = type {
	i32, ; uint32_t token
	ptr ; MonoClass klass
}

@assembly_image_cache = dso_local local_unnamed_addr global [339 x ptr] zeroinitializer, align 16

; Each entry maps hash of an assembly name to an index into the `assembly_image_cache` array
@assembly_image_cache_hashes = dso_local local_unnamed_addr constant [1017 x i64] [
	i64 u0x001e58127c546039, ; 0: lib_System.Globalization.dll.so => 47
	i64 u0x0024d0f62dee05bd, ; 1: Xamarin.KotlinX.Coroutines.Core.dll => 300
	i64 u0x0071cf2d27b7d61e, ; 2: lib_Xamarin.AndroidX.SwipeRefreshLayout.dll.so => 275
	i64 u0x01109b0e4d99e61f, ; 3: System.ComponentModel.Annotations.dll => 18
	i64 u0x02123411c4e01926, ; 4: lib_Xamarin.AndroidX.Navigation.Runtime.dll.so => 263
	i64 u0x0284512fad379f7e, ; 5: System.Runtime.Handles => 112
	i64 u0x02abedc11addc1ed, ; 6: lib_Mono.Android.Runtime.dll.so => 179
	i64 u0x02f55bf70672f5c8, ; 7: lib_System.IO.FileSystem.DriveInfo.dll.so => 53
	i64 u0x032267b2a94db371, ; 8: lib_Xamarin.AndroidX.AppCompat.dll.so => 217
	i64 u0x03621c804933a890, ; 9: System.Buffers => 12
	i64 u0x0363ac97a4cb84e6, ; 10: SQLitePCLRaw.provider.e_sqlite3.dll => 207
	i64 u0x0399610510a38a38, ; 11: lib_System.Private.DataContractSerialization.dll.so => 93
	i64 u0x043032f1d071fae0, ; 12: ru/Microsoft.Maui.Controls.resources => 328
	i64 u0x044440a55165631e, ; 13: lib-cs-Microsoft.Maui.Controls.resources.dll.so => 306
	i64 u0x046eb1581a80c6b0, ; 14: vi/Microsoft.Maui.Controls.resources => 334
	i64 u0x047408741db2431a, ; 15: Xamarin.AndroidX.DynamicAnimation => 237
	i64 u0x0517ef04e06e9f76, ; 16: System.Net.Primitives => 77
	i64 u0x051a3be159e4ef99, ; 17: Xamarin.GooglePlayServices.Tasks => 295
	i64 u0x0565d18c6da3de38, ; 18: Xamarin.AndroidX.RecyclerView => 267
	i64 u0x0581db89237110e9, ; 19: lib_System.Collections.dll.so => 17
	i64 u0x05989cb940b225a9, ; 20: Microsoft.Maui.dll => 199
	i64 u0x05a1c25e78e22d87, ; 21: lib_System.Runtime.CompilerServices.Unsafe.dll.so => 109
	i64 u0x06076b5d2b581f08, ; 22: zh-HK/Microsoft.Maui.Controls.resources => 335
	i64 u0x06388ffe9f6c161a, ; 23: System.Xml.Linq.dll => 164
	i64 u0x06600c4c124cb358, ; 24: System.Configuration.dll => 24
	i64 u0x067f95c5ddab55b3, ; 25: lib_Xamarin.AndroidX.Fragment.Ktx.dll.so => 242
	i64 u0x0680a433c781bb3d, ; 26: Xamarin.AndroidX.Collection.Jvm => 224
	i64 u0x069fff96ec92a91d, ; 27: System.Xml.XPath.dll => 169
	i64 u0x070b0847e18dab68, ; 28: Xamarin.AndroidX.Emoji2.ViewsHelper.dll => 239
	i64 u0x0739448d84d3b016, ; 29: lib_Xamarin.AndroidX.VectorDrawable.dll.so => 279
	i64 u0x07469f2eecce9e85, ; 30: mscorlib.dll => 175
	i64 u0x07c57877c7ba78ad, ; 31: ru/Microsoft.Maui.Controls.resources.dll => 328
	i64 u0x07dcdc7460a0c5e4, ; 32: System.Collections.NonGeneric => 15
	i64 u0x08122e52765333c8, ; 33: lib_Microsoft.Extensions.Logging.Debug.dll.so => 193
	i64 u0x088610fc2509f69e, ; 34: lib_Xamarin.AndroidX.VectorDrawable.Animated.dll.so => 280
	i64 u0x08a7c865576bbde7, ; 35: System.Reflection.Primitives => 103
	i64 u0x08c9d051a4a817e5, ; 36: Xamarin.AndroidX.CustomView.PoolingContainer.dll => 235
	i64 u0x08f3c9788ee2153c, ; 37: Xamarin.AndroidX.DrawerLayout => 236
	i64 u0x09138715c92dba90, ; 38: lib_System.ComponentModel.Annotations.dll.so => 18
	i64 u0x0919c28b89381a0b, ; 39: lib_Microsoft.Extensions.Options.dll.so => 194
	i64 u0x092266563089ae3e, ; 40: lib_System.Collections.NonGeneric.dll.so => 15
	i64 u0x098b50f911ccea8d, ; 41: lib_Xamarin.GooglePlayServices.Basement.dll.so => 293
	i64 u0x09d144a7e214d457, ; 42: System.Security.Cryptography => 134
	i64 u0x09e2b9f743db21a8, ; 43: lib_System.Reflection.Metadata.dll.so => 102
	i64 u0x0abb3e2b271edc45, ; 44: System.Threading.Channels.dll => 148
	i64 u0x0b06b1feab070143, ; 45: System.Formats.Tar => 44
	i64 u0x0b3b632c3bbee20c, ; 46: sk/Microsoft.Maui.Controls.resources => 329
	i64 u0x0b6aff547b84fbe9, ; 47: Xamarin.KotlinX.Serialization.Core.Jvm => 303
	i64 u0x0be2e1f8ce4064ed, ; 48: Xamarin.AndroidX.ViewPager => 282
	i64 u0x0c3ca6cc978e2aae, ; 49: pt-BR/Microsoft.Maui.Controls.resources => 325
	i64 u0x0c3dd9438f54f672, ; 50: lib_Xamarin.GooglePlayServices.Maps.dll.so => 294
	i64 u0x0c59ad9fbbd43abe, ; 51: Mono.Android => 180
	i64 u0x0c65741e86371ee3, ; 52: lib_Xamarin.Android.Glide.GifDecoder.dll.so => 211
	i64 u0x0c74af560004e816, ; 53: Microsoft.Win32.Registry.dll => 10
	i64 u0x0c7790f60165fc06, ; 54: lib_Microsoft.Maui.Essentials.dll.so => 200
	i64 u0x0c83c82812e96127, ; 55: lib_System.Net.Mail.dll.so => 73
	i64 u0x0cce4bce83380b7f, ; 56: Xamarin.AndroidX.Security.SecurityCrypto => 272
	i64 u0x0d13cd7cce4284e4, ; 57: System.Security.SecureString => 137
	i64 u0x0d63f4f73521c24f, ; 58: lib_Xamarin.AndroidX.SavedState.SavedState.Ktx.dll.so => 271
	i64 u0x0e04e702012f8463, ; 59: Xamarin.AndroidX.Emoji2 => 238
	i64 u0x0e14e73a54dda68e, ; 60: lib_System.Net.NameResolution.dll.so => 74
	i64 u0x0f37dd7a62ae99af, ; 61: lib_Xamarin.AndroidX.Collection.Ktx.dll.so => 225
	i64 u0x0f5e7abaa7cf470a, ; 62: System.Net.HttpListener => 72
	i64 u0x1001f97bbe242e64, ; 63: System.IO.UnmanagedMemoryStream => 62
	i64 u0x102a31b45304b1da, ; 64: Xamarin.AndroidX.CustomView => 234
	i64 u0x1065c4cb554c3d75, ; 65: System.IO.IsolatedStorage.dll => 57
	i64 u0x10f6cfcbcf801616, ; 66: System.IO.Compression.Brotli => 48
	i64 u0x114443cdcf2091f1, ; 67: System.Security.Cryptography.Primitives => 132
	i64 u0x11a603952763e1d4, ; 68: System.Net.Mail => 73
	i64 u0x11a70d0e1009fb11, ; 69: System.Net.WebSockets.dll => 88
	i64 u0x11f26371eee0d3c1, ; 70: lib_Xamarin.AndroidX.Lifecycle.Runtime.Ktx.dll.so => 252
	i64 u0x12128b3f59302d47, ; 71: lib_System.Xml.Serialization.dll.so => 166
	i64 u0x123639456fb056da, ; 72: System.Reflection.Emit.Lightweight.dll => 99
	i64 u0x12521e9764603eaa, ; 73: lib_System.Resources.Reader.dll.so => 106
	i64 u0x125b7f94acb989db, ; 74: Xamarin.AndroidX.RecyclerView.dll => 267
	i64 u0x12d3b63863d4ab0b, ; 75: lib_System.Threading.Overlapped.dll.so => 149
	i64 u0x134eab1061c395ee, ; 76: System.Transactions => 159
	i64 u0x138567fa954faa55, ; 77: Xamarin.AndroidX.Browser => 221
	i64 u0x13a01de0cbc3f06c, ; 78: lib-fr-Microsoft.Maui.Controls.resources.dll.so => 312
	i64 u0x13beedefb0e28a45, ; 79: lib_System.Xml.XmlDocument.dll.so => 170
	i64 u0x13f1e5e209e91af4, ; 80: lib_Java.Interop.dll.so => 177
	i64 u0x13f1e880c25d96d1, ; 81: he/Microsoft.Maui.Controls.resources => 313
	i64 u0x143d8ea60a6a4011, ; 82: Microsoft.Extensions.DependencyInjection.Abstractions => 187
	i64 u0x1497051b917530bd, ; 83: lib_System.Net.WebSockets.dll.so => 88
	i64 u0x14d612a531c79c05, ; 84: Xamarin.JSpecify.dll => 297
	i64 u0x14e68447938213b7, ; 85: Xamarin.AndroidX.Collection.Ktx.dll => 225
	i64 u0x152a448bd1e745a7, ; 86: Microsoft.Win32.Primitives => 9
	i64 u0x1557de0138c445f4, ; 87: lib_Microsoft.Win32.Registry.dll.so => 10
	i64 u0x15bdc156ed462f2f, ; 88: lib_System.IO.FileSystem.dll.so => 56
	i64 u0x15e300c2c1668655, ; 89: System.Resources.Writer.dll => 108
	i64 u0x16bf2a22df043a09, ; 90: System.IO.Pipes.dll => 61
	i64 u0x16ea2b318ad2d830, ; 91: System.Security.Cryptography.Algorithms => 127
	i64 u0x16eeae54c7ebcc08, ; 92: System.Reflection.dll => 105
	i64 u0x17125c9a85b4929f, ; 93: lib_netstandard.dll.so => 176
	i64 u0x1716866f7416792e, ; 94: lib_System.Security.AccessControl.dll.so => 125
	i64 u0x174f71c46216e44a, ; 95: Xamarin.KotlinX.Coroutines.Core => 300
	i64 u0x1752c12f1e1fc00c, ; 96: System.Core => 26
	i64 u0x17b56e25558a5d36, ; 97: lib-hu-Microsoft.Maui.Controls.resources.dll.so => 316
	i64 u0x17f9358913beb16a, ; 98: System.Text.Encodings.Web => 144
	i64 u0x1809fb23f29ba44a, ; 99: lib_System.Reflection.TypeExtensions.dll.so => 104
	i64 u0x18402a709e357f3b, ; 100: lib_Xamarin.KotlinX.Serialization.Core.Jvm.dll.so => 303
	i64 u0x18a9befae51bb361, ; 101: System.Net.WebClient => 84
	i64 u0x18f0ce884e87d89a, ; 102: nb/Microsoft.Maui.Controls.resources.dll => 322
	i64 u0x19777fba3c41b398, ; 103: Xamarin.AndroidX.Startup.StartupRuntime.dll => 274
	i64 u0x19a4c090f14ebb66, ; 104: System.Security.Claims => 126
	i64 u0x1a91866a319e9259, ; 105: lib_System.Collections.Concurrent.dll.so => 13
	i64 u0x1aac34d1917ba5d3, ; 106: lib_System.dll.so => 173
	i64 u0x1aad60783ffa3e5b, ; 107: lib-th-Microsoft.Maui.Controls.resources.dll.so => 331
	i64 u0x1aea8f1c3b282172, ; 108: lib_System.Net.Ping.dll.so => 76
	i64 u0x1b4b7a1d0d265fa2, ; 109: Xamarin.Android.Glide.DiskLruCache => 210
	i64 u0x1bbdb16cfa73e785, ; 110: Xamarin.AndroidX.Lifecycle.Runtime.Ktx.Android => 253
	i64 u0x1bc766e07b2b4241, ; 111: Xamarin.AndroidX.ResourceInspection.Annotation.dll => 268
	i64 u0x1c44c38657f2e3fa, ; 112: zh/FoodStreetMobile.resources => 4
	i64 u0x1c753b5ff15bce1b, ; 113: Mono.Android.Runtime.dll => 179
	i64 u0x1cd47467799d8250, ; 114: System.Threading.Tasks.dll => 153
	i64 u0x1d23eafdc6dc346c, ; 115: System.Globalization.Calendars.dll => 45
	i64 u0x1da4110562816681, ; 116: Xamarin.AndroidX.Security.SecurityCrypto.dll => 272
	i64 u0x1db6820994506bf5, ; 117: System.IO.FileSystem.AccessControl.dll => 52
	i64 u0x1dbb0c2c6a999acb, ; 118: System.Diagnostics.StackTrace => 35
	i64 u0x1e3d87657e9659bc, ; 119: Xamarin.AndroidX.Navigation.UI => 265
	i64 u0x1e71143913d56c10, ; 120: lib-ko-Microsoft.Maui.Controls.resources.dll.so => 320
	i64 u0x1e7c31185e2fb266, ; 121: lib_System.Threading.Tasks.Parallel.dll.so => 152
	i64 u0x1ebf5f61660de5b4, ; 122: ko/FoodStreetMobile.resources.dll => 2
	i64 u0x1ed8fcce5e9b50a0, ; 123: Microsoft.Extensions.Options.dll => 194
	i64 u0x1f055d15d807e1b2, ; 124: System.Xml.XmlSerializer => 171
	i64 u0x1f1ed22c1085f044, ; 125: lib_System.Diagnostics.FileVersionInfo.dll.so => 33
	i64 u0x1f61df9c5b94d2c1, ; 126: lib_System.Numerics.dll.so => 91
	i64 u0x1f750bb5421397de, ; 127: lib_Xamarin.AndroidX.Tracing.Tracing.dll.so => 276
	i64 u0x20237ea48006d7a8, ; 128: lib_System.Net.WebClient.dll.so => 84
	i64 u0x209375905fcc1bad, ; 129: lib_System.IO.Compression.Brotli.dll.so => 48
	i64 u0x20fab3cf2dfbc8df, ; 130: lib_System.Diagnostics.Process.dll.so => 34
	i64 u0x2110167c128cba15, ; 131: System.Globalization => 47
	i64 u0x21419508838f7547, ; 132: System.Runtime.CompilerServices.VisualC => 110
	i64 u0x2144c8860909e23b, ; 133: zh/FoodStreetMobile.resources.dll => 4
	i64 u0x2174319c0d835bc9, ; 134: System.Runtime => 124
	i64 u0x2198e5bc8b7153fa, ; 135: Xamarin.AndroidX.Annotation.Experimental.dll => 215
	i64 u0x219ea1b751a4dee4, ; 136: lib_System.IO.Compression.ZipFile.dll.so => 50
	i64 u0x21cc7e445dcd5469, ; 137: System.Reflection.Emit.ILGeneration => 98
	i64 u0x220fd4f2e7c48170, ; 138: th/Microsoft.Maui.Controls.resources => 331
	i64 u0x224538d85ed15a82, ; 139: System.IO.Pipes => 61
	i64 u0x22908438c6bed1af, ; 140: lib_System.Threading.Timer.dll.so => 156
	i64 u0x2347c268e3e4e536, ; 141: Xamarin.GooglePlayServices.Basement.dll => 293
	i64 u0x237be844f1f812c7, ; 142: System.Threading.Thread.dll => 154
	i64 u0x23852b3bdc9f7096, ; 143: System.Resources.ResourceManager => 107
	i64 u0x23986dd7e5d4fc01, ; 144: System.IO.FileSystem.Primitives.dll => 54
	i64 u0x2407aef2bbe8fadf, ; 145: System.Console => 25
	i64 u0x240abe014b27e7d3, ; 146: Xamarin.AndroidX.Core.dll => 230
	i64 u0x247619fe4413f8bf, ; 147: System.Runtime.Serialization.Primitives.dll => 121
	i64 u0x24de8d301281575e, ; 148: Xamarin.Android.Glide => 208
	i64 u0x252073cc3caa62c2, ; 149: fr/Microsoft.Maui.Controls.resources.dll => 312
	i64 u0x256b8d41255f01b1, ; 150: Xamarin.Google.Crypto.Tink.Android => 289
	i64 u0x25a0a7eff76ea08e, ; 151: SQLitePCLRaw.batteries_v2.dll => 204
	i64 u0x2662c629b96b0b30, ; 152: lib_Xamarin.Kotlin.StdLib.dll.so => 298
	i64 u0x268c1439f13bcc29, ; 153: lib_Microsoft.Extensions.Primitives.dll.so => 195
	i64 u0x26a670e154a9c54b, ; 154: System.Reflection.Extensions.dll => 101
	i64 u0x26d077d9678fe34f, ; 155: System.IO.dll => 63
	i64 u0x273f3515de5faf0d, ; 156: id/Microsoft.Maui.Controls.resources.dll => 317
	i64 u0x2742545f9094896d, ; 157: hr/Microsoft.Maui.Controls.resources => 315
	i64 u0x274d85d83ad40513, ; 158: lib_Xamarin.AndroidX.Window.WindowCore.dll.so => 285
	i64 u0x2759af78ab94d39b, ; 159: System.Net.WebSockets => 88
	i64 u0x27b2b16f3e9de038, ; 160: Xamarin.Google.Crypto.Tink.Android.dll => 289
	i64 u0x27b410442fad6cf1, ; 161: Java.Interop.dll => 177
	i64 u0x27b97e0d52c3034a, ; 162: System.Diagnostics.Debug => 31
	i64 u0x2801845a2c71fbfb, ; 163: System.Net.Primitives.dll => 77
	i64 u0x286835e259162700, ; 164: lib_Xamarin.AndroidX.ProfileInstaller.ProfileInstaller.dll.so => 266
	i64 u0x28e52865585a1ebe, ; 165: Microsoft.Extensions.Diagnostics.Abstractions.dll => 188
	i64 u0x2949f3617a02c6b2, ; 166: Xamarin.AndroidX.ExifInterface => 240
	i64 u0x29aeab763a527e52, ; 167: lib_Xamarin.AndroidX.Navigation.Common.Android.dll.so => 261
	i64 u0x2a128783efe70ba0, ; 168: uk/Microsoft.Maui.Controls.resources.dll => 333
	i64 u0x2a3b095612184159, ; 169: lib_System.Net.NetworkInformation.dll.so => 75
	i64 u0x2a6507a5ffabdf28, ; 170: System.Diagnostics.TraceSource.dll => 38
	i64 u0x2ad156c8e1354139, ; 171: fi/Microsoft.Maui.Controls.resources => 311
	i64 u0x2ad5d6b13b7a3e04, ; 172: System.ComponentModel.DataAnnotations.dll => 19
	i64 u0x2af298f63581d886, ; 173: System.Text.RegularExpressions.dll => 146
	i64 u0x2afc1c4f898552ee, ; 174: lib_System.Formats.Asn1.dll.so => 43
	i64 u0x2b148910ed40fbf9, ; 175: zh-Hant/Microsoft.Maui.Controls.resources.dll => 337
	i64 u0x2b6989d78cba9a15, ; 176: Xamarin.AndroidX.Concurrent.Futures.dll => 226
	i64 u0x2c8bd14bb93a7d82, ; 177: lib-pl-Microsoft.Maui.Controls.resources.dll.so => 324
	i64 u0x2cbd9262ca785540, ; 178: lib_System.Text.Encoding.CodePages.dll.so => 141
	i64 u0x2cc9e1fed6257257, ; 179: lib_System.Reflection.Emit.Lightweight.dll.so => 99
	i64 u0x2cd723e9fe623c7c, ; 180: lib_System.Private.Xml.Linq.dll.so => 95
	i64 u0x2ce03196fe1170d2, ; 181: Microsoft.Maui.Controls.Maps.dll => 197
	i64 u0x2d169d318a968379, ; 182: System.Threading.dll => 157
	i64 u0x2d20145f27cfc1d2, ; 183: Xamarin.AndroidX.Window.WindowCore.Jvm.dll => 286
	i64 u0x2d47774b7d993f59, ; 184: sv/Microsoft.Maui.Controls.resources.dll => 330
	i64 u0x2d5ffcae1ad0aaca, ; 185: System.Data.dll => 29
	i64 u0x2db915caf23548d2, ; 186: System.Text.Json.dll => 145
	i64 u0x2dcaa0bb15a4117a, ; 187: System.IO.UnmanagedMemoryStream.dll => 62
	i64 u0x2e2ced2c3c6a1edc, ; 188: lib_System.Threading.AccessControl.dll.so => 147
	i64 u0x2e5a40c319acb800, ; 189: System.IO.FileSystem => 56
	i64 u0x2e6f1f226821322a, ; 190: el/Microsoft.Maui.Controls.resources.dll => 309
	i64 u0x2f02f94df3200fe5, ; 191: System.Diagnostics.Process => 34
	i64 u0x2f2e98e1c89b1aff, ; 192: System.Xml.ReaderWriter => 165
	i64 u0x2f5911d9ba814e4e, ; 193: System.Diagnostics.Tracing => 39
	i64 u0x2f84070a459bc31f, ; 194: lib_System.Xml.dll.so => 172
	i64 u0x309ee9eeec09a71e, ; 195: lib_Xamarin.AndroidX.Fragment.dll.so => 241
	i64 u0x30bde19041cd89dd, ; 196: lib_Microsoft.Maui.Maps.dll.so => 202
	i64 u0x30c6dda129408828, ; 197: System.IO.IsolatedStorage => 57
	i64 u0x31195fef5d8fb552, ; 198: _Microsoft.Android.Resource.Designer.dll => 338
	i64 u0x312c8ed623cbfc8d, ; 199: Xamarin.AndroidX.Window.dll => 284
	i64 u0x31496b779ed0663d, ; 200: lib_System.Reflection.DispatchProxy.dll.so => 97
	i64 u0x32243413e774362a, ; 201: Xamarin.AndroidX.CardView.dll => 222
	i64 u0x3235427f8d12dae1, ; 202: lib_System.Drawing.Primitives.dll.so => 40
	i64 u0x329753a17a517811, ; 203: fr/Microsoft.Maui.Controls.resources => 312
	i64 u0x32aa989ff07a84ff, ; 204: lib_System.Xml.ReaderWriter.dll.so => 165
	i64 u0x3319cc9d416626c0, ; 205: lib-ja-FoodStreetMobile.resources.dll.so => 1
	i64 u0x33829542f112d59b, ; 206: System.Collections.Immutable => 14
	i64 u0x33a31443733849fe, ; 207: lib-es-Microsoft.Maui.Controls.resources.dll.so => 310
	i64 u0x341abc357fbb4ebf, ; 208: lib_System.Net.Sockets.dll.so => 83
	i64 u0x346a212343615ac5, ; 209: lib_System.Linq.AsyncEnumerable.dll.so => 64
	i64 u0x3496c1e2dcaf5ecc, ; 210: lib_System.IO.Pipes.AccessControl.dll.so => 60
	i64 u0x34dfd74fe2afcf37, ; 211: Microsoft.Maui => 199
	i64 u0x34e292762d9615df, ; 212: cs/Microsoft.Maui.Controls.resources.dll => 306
	i64 u0x34fa1ae947be99b8, ; 213: en/FoodStreetMobile.resources => 0
	i64 u0x3508234247f48404, ; 214: Microsoft.Maui.Controls => 196
	i64 u0x353590da528c9d22, ; 215: System.ComponentModel.Annotations => 18
	i64 u0x3549870798b4cd30, ; 216: lib_Xamarin.AndroidX.ViewPager2.dll.so => 283
	i64 u0x355282fc1c909694, ; 217: Microsoft.Extensions.Configuration => 184
	i64 u0x3552fc5d578f0fbf, ; 218: Xamarin.AndroidX.Arch.Core.Common => 219
	i64 u0x355c649948d55d97, ; 219: lib_System.Runtime.Intrinsics.dll.so => 116
	i64 u0x35ea9d1c6834bc8c, ; 220: Xamarin.AndroidX.Lifecycle.ViewModel.Ktx.dll => 256
	i64 u0x3628ab68db23a01a, ; 221: lib_System.Diagnostics.Tools.dll.so => 37
	i64 u0x3673b042508f5b6b, ; 222: lib_System.Runtime.Extensions.dll.so => 111
	i64 u0x36740f1a8ecdc6c4, ; 223: System.Numerics => 91
	i64 u0x36b2b50fdf589ae2, ; 224: System.Reflection.Emit.Lightweight => 99
	i64 u0x36cada77dc79928b, ; 225: System.IO.MemoryMappedFiles => 58
	i64 u0x374ef46b06791af6, ; 226: System.Reflection.Primitives.dll => 103
	i64 u0x376bf93e521a5417, ; 227: lib_Xamarin.Jetbrains.Annotations.dll.so => 296
	i64 u0x37bc29f3183003b6, ; 228: lib_System.IO.dll.so => 63
	i64 u0x380134e03b1e160a, ; 229: System.Collections.Immutable.dll => 14
	i64 u0x38049b5c59b39324, ; 230: System.Runtime.CompilerServices.Unsafe => 109
	i64 u0x385c17636bb6fe6e, ; 231: Xamarin.AndroidX.CustomView.dll => 234
	i64 u0x38869c811d74050e, ; 232: System.Net.NameResolution.dll => 74
	i64 u0x393c226616977fdb, ; 233: lib_Xamarin.AndroidX.ViewPager.dll.so => 282
	i64 u0x395e37c3334cf82a, ; 234: lib-ca-Microsoft.Maui.Controls.resources.dll.so => 305
	i64 u0x39c3107c28752af1, ; 235: lib_Microsoft.Extensions.FileProviders.Abstractions.dll.so => 189
	i64 u0x3ab5859054645f72, ; 236: System.Security.Cryptography.Primitives.dll => 132
	i64 u0x3ad75090c3fac0e9, ; 237: lib_Xamarin.AndroidX.ResourceInspection.Annotation.dll.so => 268
	i64 u0x3ae44ac43a1fbdbb, ; 238: System.Runtime.Serialization => 123
	i64 u0x3b860f9932505633, ; 239: lib_System.Text.Encoding.Extensions.dll.so => 142
	i64 u0x3be99b43dd39dd37, ; 240: Xamarin.AndroidX.SavedState.SavedState.Android => 270
	i64 u0x3c3aafb6b3a00bf6, ; 241: lib_System.Security.Cryptography.X509Certificates.dll.so => 133
	i64 u0x3c4049146b59aa90, ; 242: System.Runtime.InteropServices.JavaScript => 113
	i64 u0x3c7c495f58ac5ee9, ; 243: Xamarin.Kotlin.StdLib => 298
	i64 u0x3c7e5ed3d5db71bb, ; 244: System.Security => 138
	i64 u0x3cd9d281d402eb9b, ; 245: Xamarin.AndroidX.Browser.dll => 221
	i64 u0x3d1c50cc001a991e, ; 246: Xamarin.Google.Guava.ListenableFuture.dll => 291
	i64 u0x3d2b1913edfc08d7, ; 247: lib_System.Threading.ThreadPool.dll.so => 155
	i64 u0x3d46f0b995082740, ; 248: System.Xml.Linq => 164
	i64 u0x3d8a8f400514a790, ; 249: Xamarin.AndroidX.Fragment.Ktx.dll => 242
	i64 u0x3d9c2a242b040a50, ; 250: lib_Xamarin.AndroidX.Core.dll.so => 230
	i64 u0x3da7781d6333a8fe, ; 251: SQLitePCLRaw.batteries_v2 => 204
	i64 u0x3dbb6b9f5ab90fa7, ; 252: lib_Xamarin.AndroidX.DynamicAnimation.dll.so => 237
	i64 u0x3e5441657549b213, ; 253: Xamarin.AndroidX.ResourceInspection.Annotation => 268
	i64 u0x3e57d4d195c53c2e, ; 254: System.Reflection.TypeExtensions => 104
	i64 u0x3e616ab4ed1f3f15, ; 255: lib_System.Data.dll.so => 29
	i64 u0x3f1d226e6e06db7e, ; 256: Xamarin.AndroidX.SlidingPaneLayout.dll => 273
	i64 u0x3f510adf788828dd, ; 257: System.Threading.Tasks.Extensions => 151
	i64 u0x3f6f5914291cdcf7, ; 258: Microsoft.Extensions.Hosting.Abstractions => 190
	i64 u0x407a10bb4bf95829, ; 259: lib_Xamarin.AndroidX.Navigation.Common.dll.so => 260
	i64 u0x40c98b6bd77346d4, ; 260: Microsoft.VisualBasic.dll => 8
	i64 u0x41833cf766d27d96, ; 261: mscorlib => 175
	i64 u0x41cab042be111c34, ; 262: lib_Xamarin.AndroidX.AppCompat.AppCompatResources.dll.so => 218
	i64 u0x423a9ecc4d905a88, ; 263: lib_System.Resources.ResourceManager.dll.so => 107
	i64 u0x423bf51ae7def810, ; 264: System.Xml.XPath => 169
	i64 u0x42462ff15ddba223, ; 265: System.Resources.Reader.dll => 106
	i64 u0x4291015ff4e5ef71, ; 266: Xamarin.AndroidX.Core.ViewTree.dll => 232
	i64 u0x42a31b86e6ccc3f0, ; 267: System.Diagnostics.Contracts => 30
	i64 u0x430e95b891249788, ; 268: lib_System.Reflection.Emit.dll.so => 100
	i64 u0x43375950ec7c1b6a, ; 269: netstandard.dll => 176
	i64 u0x434c4e1d9284cdae, ; 270: Mono.Android.dll => 180
	i64 u0x43505013578652a0, ; 271: lib_Xamarin.AndroidX.Activity.Ktx.dll.so => 213
	i64 u0x437d06c381ed575a, ; 272: lib_Microsoft.VisualBasic.dll.so => 8
	i64 u0x43950f84de7cc79a, ; 273: pl/Microsoft.Maui.Controls.resources.dll => 324
	i64 u0x43c077442b230f64, ; 274: Xamarin.AndroidX.Tracing.Tracing.Android => 277
	i64 u0x43e8ca5bc927ff37, ; 275: lib_Xamarin.AndroidX.Emoji2.ViewsHelper.dll.so => 239
	i64 u0x448bd33429269b19, ; 276: Microsoft.CSharp => 6
	i64 u0x4499fa3c8e494654, ; 277: lib_System.Runtime.Serialization.Primitives.dll.so => 121
	i64 u0x4515080865a951a5, ; 278: Xamarin.Kotlin.StdLib.dll => 298
	i64 u0x4545802489b736b9, ; 279: Xamarin.AndroidX.Fragment.Ktx => 242
	i64 u0x454b4d1e66bb783c, ; 280: Xamarin.AndroidX.Lifecycle.Process => 249
	i64 u0x45c40276a42e283e, ; 281: System.Diagnostics.TraceSource => 38
	i64 u0x45d443f2a29adc37, ; 282: System.AppContext.dll => 11
	i64 u0x4659803ed689655d, ; 283: ru/FoodStreetMobile.resources.dll => 3
	i64 u0x46a4213bc97fe5ae, ; 284: lib-ru-Microsoft.Maui.Controls.resources.dll.so => 328
	i64 u0x47358bd471172e1d, ; 285: lib_System.Xml.Linq.dll.so => 164
	i64 u0x47daf4e1afbada10, ; 286: pt/Microsoft.Maui.Controls.resources => 326
	i64 u0x480c0a47dd42dd81, ; 287: lib_System.IO.MemoryMappedFiles.dll.so => 58
	i64 u0x49e952f19a4e2022, ; 288: System.ObjectModel => 92
	i64 u0x49f9e6948a8131e4, ; 289: lib_Xamarin.AndroidX.VersionedParcelable.dll.so => 281
	i64 u0x4a5667b2462a664b, ; 290: lib_Xamarin.AndroidX.Navigation.UI.dll.so => 265
	i64 u0x4a5e9fe1879c6fa2, ; 291: ru/FoodStreetMobile.resources => 3
	i64 u0x4a7a18981dbd56bc, ; 292: System.IO.Compression.FileSystem.dll => 49
	i64 u0x4aa5c60350917c06, ; 293: lib_Xamarin.AndroidX.Lifecycle.LiveData.Core.Ktx.dll.so => 248
	i64 u0x4b07a0ed0ab33ff4, ; 294: System.Runtime.Extensions.dll => 111
	i64 u0x4b576d47ac054f3c, ; 295: System.IO.FileSystem.AccessControl => 52
	i64 u0x4b7b6532ded934b7, ; 296: System.Text.Json => 145
	i64 u0x4c2029a97af23a8d, ; 297: Xamarin.AndroidX.Lifecycle.ViewModelSavedState.Android => 258
	i64 u0x4c7755cf07ad2d5f, ; 298: System.Net.Http.Json.dll => 70
	i64 u0x4c9caee94c082049, ; 299: Xamarin.GooglePlayServices.Maps => 294
	i64 u0x4cc5f15266470798, ; 300: lib_Xamarin.AndroidX.Loader.dll.so => 259
	i64 u0x4cf6f67dc77aacd2, ; 301: System.Net.NetworkInformation.dll => 75
	i64 u0x4d3183dd245425d4, ; 302: System.Net.WebSockets.Client.dll => 87
	i64 u0x4d479f968a05e504, ; 303: System.Linq.Expressions.dll => 65
	i64 u0x4d55a010ffc4faff, ; 304: System.Private.Xml => 96
	i64 u0x4d5cbe77561c5b2e, ; 305: System.Web.dll => 162
	i64 u0x4d77512dbd86ee4c, ; 306: lib_Xamarin.AndroidX.Arch.Core.Common.dll.so => 219
	i64 u0x4d7793536e79c309, ; 307: System.ServiceProcess => 140
	i64 u0x4d95fccc1f67c7ca, ; 308: System.Runtime.Loader.dll => 117
	i64 u0x4db014bf0ff1c9c1, ; 309: System.Linq.AsyncEnumerable => 64
	i64 u0x4dcf44c3c9b076a2, ; 310: it/Microsoft.Maui.Controls.resources.dll => 318
	i64 u0x4dd9247f1d2c3235, ; 311: Xamarin.AndroidX.Loader.dll => 259
	i64 u0x4e0a67184e7325f0, ; 312: FoodStreetMobile => 5
	i64 u0x4e2aeee78e2c4a87, ; 313: Xamarin.AndroidX.ProfileInstaller.ProfileInstaller => 266
	i64 u0x4e32f00cb0937401, ; 314: Mono.Android.Runtime => 179
	i64 u0x4e5eea4668ac2b18, ; 315: System.Text.Encoding.CodePages => 141
	i64 u0x4ebd0c4b82c5eefc, ; 316: lib_System.Threading.Channels.dll.so => 148
	i64 u0x4ee8eaa9c9c1151a, ; 317: System.Globalization.Calendars => 45
	i64 u0x4f21ee6ef9eb527e, ; 318: ca/Microsoft.Maui.Controls.resources => 305
	i64 u0x4fd5f3ee53d0a4f0, ; 319: SQLitePCLRaw.lib.e_sqlite3.android => 206
	i64 u0x5037f0be3c28c7a3, ; 320: lib_Microsoft.Maui.Controls.dll.so => 196
	i64 u0x50c3a29b21050d45, ; 321: System.Linq.Parallel.dll => 66
	i64 u0x5131bbe80989093f, ; 322: Xamarin.AndroidX.Lifecycle.ViewModel.Android.dll => 255
	i64 u0x516324a5050a7e3c, ; 323: System.Net.WebProxy => 86
	i64 u0x516d6f0b21a303de, ; 324: lib_System.Diagnostics.Contracts.dll.so => 30
	i64 u0x51bb8a2afe774e32, ; 325: System.Drawing => 41
	i64 u0x5247c5c32a4140f0, ; 326: System.Resources.Reader => 106
	i64 u0x526bb15e3c386364, ; 327: Xamarin.AndroidX.Lifecycle.Runtime.Ktx.dll => 252
	i64 u0x526ce79eb8e90527, ; 328: lib_System.Net.Primitives.dll.so => 77
	i64 u0x52829f00b4467c38, ; 329: lib_System.Data.Common.dll.so => 27
	i64 u0x529ffe06f39ab8db, ; 330: Xamarin.AndroidX.Core => 230
	i64 u0x52ff996554dbf352, ; 331: Microsoft.Maui.Graphics => 201
	i64 u0x535f7e40e8fef8af, ; 332: lib-sk-Microsoft.Maui.Controls.resources.dll.so => 329
	i64 u0x53978aac584c666e, ; 333: lib_System.Security.Cryptography.Cng.dll.so => 128
	i64 u0x53a96d5c86c9e194, ; 334: System.Net.NetworkInformation => 75
	i64 u0x53be1038a61e8d44, ; 335: System.Runtime.InteropServices.RuntimeInformation.dll => 114
	i64 u0x53c3014b9437e684, ; 336: lib-zh-HK-Microsoft.Maui.Controls.resources.dll.so => 335
	i64 u0x5435e6f049e9bc37, ; 337: System.Security.Claims.dll => 126
	i64 u0x54795225dd1587af, ; 338: lib_System.Runtime.dll.so => 124
	i64 u0x547a34f14e5f6210, ; 339: Xamarin.AndroidX.Lifecycle.Common.dll => 244
	i64 u0x54b851bc9b470503, ; 340: Xamarin.AndroidX.Navigation.Common.Android => 261
	i64 u0x556e8b63b660ab8b, ; 341: Xamarin.AndroidX.Lifecycle.Common.Jvm.dll => 245
	i64 u0x5588627c9a108ec9, ; 342: System.Collections.Specialized => 16
	i64 u0x55a898e4f42e3fae, ; 343: Microsoft.VisualBasic.Core.dll => 7
	i64 u0x55fa0c610fe93bb1, ; 344: lib_System.Security.Cryptography.OpenSsl.dll.so => 131
	i64 u0x56442b99bc64bb47, ; 345: System.Runtime.Serialization.Xml.dll => 122
	i64 u0x56a8b26e1aeae27b, ; 346: System.Threading.Tasks.Dataflow => 150
	i64 u0x56f932d61e93c07f, ; 347: System.Globalization.Extensions => 46
	i64 u0x571c5cfbec5ae8e2, ; 348: System.Private.Uri => 94
	i64 u0x576499c9f52fea31, ; 349: Xamarin.AndroidX.Annotation => 214
	i64 u0x578cd35c91d7b347, ; 350: lib_SQLitePCLRaw.core.dll.so => 205
	i64 u0x579a06fed6eec900, ; 351: System.Private.CoreLib.dll => 182
	i64 u0x57adda3c951abb33, ; 352: Microsoft.Extensions.Hosting.Abstractions.dll => 190
	i64 u0x57c542c14049b66d, ; 353: System.Diagnostics.DiagnosticSource => 32
	i64 u0x581a8bd5cfda563e, ; 354: System.Threading.Timer => 156
	i64 u0x58601b2dda4a27b9, ; 355: lib-ja-Microsoft.Maui.Controls.resources.dll.so => 319
	i64 u0x58688d9af496b168, ; 356: Microsoft.Extensions.DependencyInjection.dll => 186
	i64 u0x588c167a79db6bfb, ; 357: lib_Xamarin.Google.ErrorProne.Annotations.dll.so => 290
	i64 u0x5906028ae5151104, ; 358: Xamarin.AndroidX.Activity.Ktx => 213
	i64 u0x595a356d23e8da9a, ; 359: lib_Microsoft.CSharp.dll.so => 6
	i64 u0x59f9e60b9475085f, ; 360: lib_Xamarin.AndroidX.Annotation.Experimental.dll.so => 215
	i64 u0x5a745f5101a75527, ; 361: lib_System.IO.Compression.FileSystem.dll.so => 49
	i64 u0x5a89a886ae30258d, ; 362: lib_Xamarin.AndroidX.CoordinatorLayout.dll.so => 229
	i64 u0x5a8f6699f4a1caa9, ; 363: lib_System.Threading.dll.so => 157
	i64 u0x5ae9cd33b15841bf, ; 364: System.ComponentModel => 23
	i64 u0x5b54391bdc6fcfe6, ; 365: System.Private.DataContractSerialization => 93
	i64 u0x5b5f0e240a06a2a2, ; 366: da/Microsoft.Maui.Controls.resources.dll => 307
	i64 u0x5b755276902c8414, ; 367: Xamarin.GooglePlayServices.Base => 292
	i64 u0x5b8109e8e14c5e3e, ; 368: System.Globalization.Extensions.dll => 46
	i64 u0x5bddd04d72a9e350, ; 369: Xamarin.AndroidX.Lifecycle.LiveData.Core.Ktx => 248
	i64 u0x5bdf16b09da116ab, ; 370: Xamarin.AndroidX.Collection => 223
	i64 u0x5c019d5266093159, ; 371: lib_Xamarin.AndroidX.Lifecycle.Runtime.Ktx.Android.dll.so => 253
	i64 u0x5c30a4a35f9cc8c4, ; 372: lib_System.Reflection.Extensions.dll.so => 101
	i64 u0x5c393624b8176517, ; 373: lib_Microsoft.Extensions.Logging.dll.so => 191
	i64 u0x5c53c29f5073b0c9, ; 374: System.Diagnostics.FileVersionInfo => 33
	i64 u0x5c87463c575c7616, ; 375: lib_System.Globalization.Extensions.dll.so => 46
	i64 u0x5d0a4a29b02d9d3c, ; 376: System.Net.WebHeaderCollection.dll => 85
	i64 u0x5d40c9b15181641f, ; 377: lib_Xamarin.AndroidX.Emoji2.dll.so => 238
	i64 u0x5d6ca10d35e9485b, ; 378: lib_Xamarin.AndroidX.Concurrent.Futures.dll.so => 226
	i64 u0x5d7ec76c1c703055, ; 379: System.Threading.Tasks.Parallel => 152
	i64 u0x5db0cbbd1028510e, ; 380: lib_System.Runtime.InteropServices.dll.so => 115
	i64 u0x5db30905d3e5013b, ; 381: Xamarin.AndroidX.Collection.Jvm.dll => 224
	i64 u0x5e467bc8f09ad026, ; 382: System.Collections.Specialized.dll => 16
	i64 u0x5e5173b3208d97e7, ; 383: System.Runtime.Handles.dll => 112
	i64 u0x5ea92fdb19ec8c4c, ; 384: System.Text.Encodings.Web.dll => 144
	i64 u0x5eb8046dd40e9ac3, ; 385: System.ComponentModel.Primitives => 21
	i64 u0x5ec272d219c9aba4, ; 386: System.Security.Cryptography.Csp.dll => 129
	i64 u0x5eee1376d94c7f5e, ; 387: System.Net.HttpListener.dll => 72
	i64 u0x5f36ccf5c6a57e24, ; 388: System.Xml.ReaderWriter.dll => 165
	i64 u0x5f4294b9b63cb842, ; 389: System.Data.Common => 27
	i64 u0x5f7399e166075632, ; 390: lib_SQLitePCLRaw.lib.e_sqlite3.android.dll.so => 206
	i64 u0x5f9a2d823f664957, ; 391: lib-el-Microsoft.Maui.Controls.resources.dll.so => 309
	i64 u0x5fa6da9c3cd8142a, ; 392: lib_Xamarin.KotlinX.Serialization.Core.dll.so => 302
	i64 u0x5fac98e0b37a5b9d, ; 393: System.Runtime.CompilerServices.Unsafe.dll => 109
	i64 u0x609f4b7b63d802d4, ; 394: lib_Microsoft.Extensions.DependencyInjection.dll.so => 186
	i64 u0x60cd4e33d7e60134, ; 395: Xamarin.KotlinX.Coroutines.Core.Jvm => 301
	i64 u0x60f62d786afcf130, ; 396: System.Memory => 69
	i64 u0x61bb78c89f867353, ; 397: System.IO => 63
	i64 u0x61be8d1299194243, ; 398: Microsoft.Maui.Controls.Xaml => 198
	i64 u0x61d2cba29557038f, ; 399: de/Microsoft.Maui.Controls.resources => 308
	i64 u0x61d88f399afb2f45, ; 400: lib_System.Runtime.Loader.dll.so => 117
	i64 u0x622eef6f9e59068d, ; 401: System.Private.CoreLib => 182
	i64 u0x639fb99a7bef11de, ; 402: Xamarin.AndroidX.Navigation.Runtime.Android.dll => 264
	i64 u0x63d5e3aa4ef9b931, ; 403: Xamarin.KotlinX.Coroutines.Android.dll => 299
	i64 u0x63f1f6883c1e23c2, ; 404: lib_System.Collections.Immutable.dll.so => 14
	i64 u0x6400f68068c1e9f1, ; 405: Xamarin.Google.Android.Material.dll => 287
	i64 u0x640e3b14dbd325c2, ; 406: System.Security.Cryptography.Algorithms.dll => 127
	i64 u0x64587004560099b9, ; 407: System.Reflection => 105
	i64 u0x64b1529a438a3c45, ; 408: lib_System.Runtime.Handles.dll.so => 112
	i64 u0x64b61dd9da8a4d57, ; 409: System.Net.ServerSentEvents.dll => 81
	i64 u0x6565fba2cd8f235b, ; 410: Xamarin.AndroidX.Lifecycle.ViewModel.Ktx => 256
	i64 u0x65ecac39144dd3cc, ; 411: Microsoft.Maui.Controls.dll => 196
	i64 u0x65ece51227bfa724, ; 412: lib_System.Runtime.Numerics.dll.so => 118
	i64 u0x661722438787b57f, ; 413: Xamarin.AndroidX.Annotation.Jvm.dll => 216
	i64 u0x6679b2337ee6b22a, ; 414: lib_System.IO.FileSystem.Primitives.dll.so => 54
	i64 u0x667c66a03dd97d40, ; 415: System.Linq.AsyncEnumerable.dll => 64
	i64 u0x6692e924eade1b29, ; 416: lib_System.Console.dll.so => 25
	i64 u0x66a4e5c6a3fb0bae, ; 417: lib_Xamarin.AndroidX.Lifecycle.ViewModel.Android.dll.so => 255
	i64 u0x66d13304ce1a3efa, ; 418: Xamarin.AndroidX.CursorAdapter => 233
	i64 u0x674303f65d8fad6f, ; 419: lib_System.Net.Quic.dll.so => 78
	i64 u0x6756ca4cad62e9d6, ; 420: lib_Xamarin.AndroidX.ConstraintLayout.Core.dll.so => 228
	i64 u0x67c0802770244408, ; 421: System.Windows.dll => 163
	i64 u0x68100b69286e27cd, ; 422: lib_System.Formats.Tar.dll.so => 44
	i64 u0x68558ec653afa616, ; 423: lib-da-Microsoft.Maui.Controls.resources.dll.so => 307
	i64 u0x6872ec7a2e36b1ac, ; 424: System.Drawing.Primitives.dll => 40
	i64 u0x68fbbbe2eb455198, ; 425: System.Formats.Asn1 => 43
	i64 u0x69063fc0ba8e6bdd, ; 426: he/Microsoft.Maui.Controls.resources.dll => 313
	i64 u0x699dffb2427a2d71, ; 427: SQLitePCLRaw.lib.e_sqlite3.android.dll => 206
	i64 u0x6a4d7577b2317255, ; 428: System.Runtime.InteropServices.dll => 115
	i64 u0x6ace3b74b15ee4a4, ; 429: nb/Microsoft.Maui.Controls.resources => 322
	i64 u0x6afcedb171067e2b, ; 430: System.Core.dll => 26
	i64 u0x6bef98e124147c24, ; 431: Xamarin.Jetbrains.Annotations => 296
	i64 u0x6ce874bff138ce2b, ; 432: Xamarin.AndroidX.Lifecycle.ViewModel.dll => 254
	i64 u0x6d12bfaa99c72b1f, ; 433: lib_Microsoft.Maui.Graphics.dll.so => 201
	i64 u0x6d70755158ca866e, ; 434: lib_System.ComponentModel.EventBasedAsync.dll.so => 20
	i64 u0x6d79993361e10ef2, ; 435: Microsoft.Extensions.Primitives => 195
	i64 u0x6d7eeca99577fc8b, ; 436: lib_System.Net.WebProxy.dll.so => 86
	i64 u0x6d8515b19946b6a2, ; 437: System.Net.WebProxy.dll => 86
	i64 u0x6d86d56b84c8eb71, ; 438: lib_Xamarin.AndroidX.CursorAdapter.dll.so => 233
	i64 u0x6d9bea6b3e895cf7, ; 439: Microsoft.Extensions.Primitives.dll => 195
	i64 u0x6e25a02c3833319a, ; 440: lib_Xamarin.AndroidX.Navigation.Fragment.dll.so => 262
	i64 u0x6e79c6bd8627412a, ; 441: Xamarin.AndroidX.SavedState.SavedState.Ktx => 271
	i64 u0x6e838d9a2a6f6c9e, ; 442: lib_System.ValueTuple.dll.so => 160
	i64 u0x6e9965ce1095e60a, ; 443: lib_System.Core.dll.so => 26
	i64 u0x6fd2265da78b93a4, ; 444: lib_Microsoft.Maui.dll.so => 199
	i64 u0x6fdfc7de82c33008, ; 445: cs/Microsoft.Maui.Controls.resources => 306
	i64 u0x6ffc4967cc47ba57, ; 446: System.IO.FileSystem.Watcher.dll => 55
	i64 u0x701cd46a1c25a5fe, ; 447: System.IO.FileSystem.dll => 56
	i64 u0x70e99f48c05cb921, ; 448: tr/Microsoft.Maui.Controls.resources.dll => 332
	i64 u0x70fd3deda22442d2, ; 449: lib-nb-Microsoft.Maui.Controls.resources.dll.so => 322
	i64 u0x71485e7ffdb4b958, ; 450: System.Reflection.Extensions => 101
	i64 u0x7162a2fce67a945f, ; 451: lib_Xamarin.Android.Glide.Annotations.dll.so => 209
	i64 u0x717530326f808838, ; 452: lib_Microsoft.Extensions.Diagnostics.Abstractions.dll.so => 188
	i64 u0x71a495ea3761dde8, ; 453: lib-it-Microsoft.Maui.Controls.resources.dll.so => 318
	i64 u0x71ad672adbe48f35, ; 454: System.ComponentModel.Primitives.dll => 21
	i64 u0x720f102581a4a5c8, ; 455: Xamarin.AndroidX.Core.ViewTree => 232
	i64 u0x725f5a9e82a45c81, ; 456: System.Security.Cryptography.Encoding => 130
	i64 u0x72b1fb4109e08d7b, ; 457: lib-hr-Microsoft.Maui.Controls.resources.dll.so => 315
	i64 u0x72e0300099accce1, ; 458: System.Xml.XPath.XDocument => 168
	i64 u0x730bfb248998f67a, ; 459: System.IO.Compression.ZipFile => 50
	i64 u0x732b2d67b9e5c47b, ; 460: Xamarin.Google.ErrorProne.Annotations.dll => 290
	i64 u0x734b76fdc0dc05bb, ; 461: lib_GoogleGson.dll.so => 183
	i64 u0x73a6be34e822f9d1, ; 462: lib_System.Runtime.Serialization.dll.so => 123
	i64 u0x73e4ce94e2eb6ffc, ; 463: lib_System.Memory.dll.so => 69
	i64 u0x743a1eccf080489a, ; 464: WindowsBase.dll => 174
	i64 u0x755a91767330b3d4, ; 465: lib_Microsoft.Extensions.Configuration.dll.so => 184
	i64 u0x75c326eb821b85c4, ; 466: lib_System.ComponentModel.DataAnnotations.dll.so => 19
	i64 u0x76012e7334db86e5, ; 467: lib_Xamarin.AndroidX.SavedState.dll.so => 269
	i64 u0x76ca07b878f44da0, ; 468: System.Runtime.Numerics.dll => 118
	i64 u0x7736c8a96e51a061, ; 469: lib_Xamarin.AndroidX.Annotation.Jvm.dll.so => 216
	i64 u0x778a805e625329ef, ; 470: System.Linq.Parallel => 66
	i64 u0x77d9074d8f33a303, ; 471: lib_System.Net.ServerSentEvents.dll.so => 81
	i64 u0x77f8a4acc2fdc449, ; 472: System.Security.Cryptography.Cng.dll => 128
	i64 u0x780bc73597a503a9, ; 473: lib-ms-Microsoft.Maui.Controls.resources.dll.so => 321
	i64 u0x782c5d8eb99ff201, ; 474: lib_Microsoft.VisualBasic.Core.dll.so => 7
	i64 u0x783606d1e53e7a1a, ; 475: th/Microsoft.Maui.Controls.resources.dll => 331
	i64 u0x78a45e51311409b6, ; 476: Xamarin.AndroidX.Fragment.dll => 241
	i64 u0x78ed4ab8f9d800a1, ; 477: Xamarin.AndroidX.Lifecycle.ViewModel => 254
	i64 u0x7a5207a7c82d30b4, ; 478: lib_Xamarin.JSpecify.dll.so => 297
	i64 u0x7a7e7eddf79c5d26, ; 479: lib_Xamarin.AndroidX.Lifecycle.ViewModel.dll.so => 254
	i64 u0x7a9a57d43b0845fa, ; 480: System.AppContext => 11
	i64 u0x7ad0f4f1e5d08183, ; 481: Xamarin.AndroidX.Collection.dll => 223
	i64 u0x7adb8da2ac89b647, ; 482: fi/Microsoft.Maui.Controls.resources.dll => 311
	i64 u0x7b13d9eaa944ade8, ; 483: Xamarin.AndroidX.DynamicAnimation.dll => 237
	i64 u0x7bef86a4335c4870, ; 484: System.ComponentModel.TypeConverter => 22
	i64 u0x7c0820144cd34d6a, ; 485: sk/Microsoft.Maui.Controls.resources.dll => 329
	i64 u0x7c2a0bd1e0f988fc, ; 486: lib-de-Microsoft.Maui.Controls.resources.dll.so => 308
	i64 u0x7c41d387501568ba, ; 487: System.Net.WebClient.dll => 84
	i64 u0x7c482cd79bd24b13, ; 488: lib_Xamarin.AndroidX.ConstraintLayout.dll.so => 227
	i64 u0x7c60acf6404e96b6, ; 489: Xamarin.AndroidX.Navigation.Common.Android.dll => 261
	i64 u0x7cb95ad2a929d044, ; 490: Xamarin.GooglePlayServices.Basement => 293
	i64 u0x7cd2ec8eaf5241cd, ; 491: System.Security.dll => 138
	i64 u0x7cf9ae50dd350622, ; 492: Xamarin.Jetbrains.Annotations.dll => 296
	i64 u0x7d649b75d580bb42, ; 493: ms/Microsoft.Maui.Controls.resources.dll => 321
	i64 u0x7d8ee2bdc8e3aad1, ; 494: System.Numerics.Vectors => 90
	i64 u0x7df5df8db8eaa6ac, ; 495: Microsoft.Extensions.Logging.Debug => 193
	i64 u0x7dfc3d6d9d8d7b70, ; 496: System.Collections => 17
	i64 u0x7e0173889caa5f5e, ; 497: lib-zh-FoodStreetMobile.resources.dll.so => 4
	i64 u0x7e2e564fa2f76c65, ; 498: lib_System.Diagnostics.Tracing.dll.so => 39
	i64 u0x7e302e110e1e1346, ; 499: lib_System.Security.Claims.dll.so => 126
	i64 u0x7e4465b3f78ad8d0, ; 500: Xamarin.KotlinX.Serialization.Core.dll => 302
	i64 u0x7e571cad5915e6c3, ; 501: lib_Xamarin.AndroidX.Lifecycle.Process.dll.so => 249
	i64 u0x7e6ac99e4e8df72f, ; 502: System.IO.Hashing => 181
	i64 u0x7e6b1ca712437d7d, ; 503: Xamarin.AndroidX.Emoji2.ViewsHelper => 239
	i64 u0x7e946809d6008ef2, ; 504: lib_System.ObjectModel.dll.so => 92
	i64 u0x7ea0272c1b4a9635, ; 505: lib_Xamarin.Android.Glide.dll.so => 208
	i64 u0x7eb4f0dc47488736, ; 506: lib_Xamarin.GooglePlayServices.Tasks.dll.so => 295
	i64 u0x7ecc13347c8fd849, ; 507: lib_System.ComponentModel.dll.so => 23
	i64 u0x7f00ddd9b9ca5a13, ; 508: Xamarin.AndroidX.ViewPager.dll => 282
	i64 u0x7f9351cd44b1273f, ; 509: Microsoft.Extensions.Configuration.Abstractions => 185
	i64 u0x7fbd557c99b3ce6f, ; 510: lib_Xamarin.AndroidX.Lifecycle.LiveData.Core.dll.so => 247
	i64 u0x8076a9a44a2ca331, ; 511: System.Net.Quic => 78
	i64 u0x80da183a87731838, ; 512: System.Reflection.Metadata => 102
	i64 u0x80fa55b6d1b0be99, ; 513: SQLitePCLRaw.provider.e_sqlite3 => 207
	i64 u0x812c069d5cdecc17, ; 514: System.dll => 173
	i64 u0x81381be520a60adb, ; 515: Xamarin.AndroidX.Interpolator.dll => 243
	i64 u0x81657cec2b31e8aa, ; 516: System.Net => 89
	i64 u0x81ab745f6c0f5ce6, ; 517: zh-Hant/Microsoft.Maui.Controls.resources => 337
	i64 u0x8277f2be6b5ce05f, ; 518: Xamarin.AndroidX.AppCompat => 217
	i64 u0x828f06563b30bc50, ; 519: lib_Xamarin.AndroidX.CardView.dll.so => 222
	i64 u0x82b399cb01b531c4, ; 520: lib_System.Web.dll.so => 162
	i64 u0x82df8f5532a10c59, ; 521: lib_System.Drawing.dll.so => 41
	i64 u0x82f0b6e911d13535, ; 522: lib_System.Transactions.dll.so => 159
	i64 u0x82f6403342e12049, ; 523: uk/Microsoft.Maui.Controls.resources => 333
	i64 u0x83144699b312ad81, ; 524: SQLite-net.dll => 203
	i64 u0x83c14ba66c8e2b8c, ; 525: zh-Hans/Microsoft.Maui.Controls.resources => 336
	i64 u0x846ce984efea52c7, ; 526: System.Threading.Tasks.Parallel.dll => 152
	i64 u0x84ae73148a4557d2, ; 527: lib_System.IO.Pipes.dll.so => 61
	i64 u0x84b01102c12a9232, ; 528: System.Runtime.Serialization.Json.dll => 120
	i64 u0x850c5ba0b57ce8e7, ; 529: lib_Xamarin.AndroidX.Collection.dll.so => 223
	i64 u0x851d02edd334b044, ; 530: Xamarin.AndroidX.VectorDrawable => 279
	i64 u0x85c919db62150978, ; 531: Xamarin.AndroidX.Transition.dll => 278
	i64 u0x8662aaeb94fef37f, ; 532: lib_System.Dynamic.Runtime.dll.so => 42
	i64 u0x86a909228dc7657b, ; 533: lib-zh-Hant-Microsoft.Maui.Controls.resources.dll.so => 337
	i64 u0x86b3e00c36b84509, ; 534: Microsoft.Extensions.Configuration.dll => 184
	i64 u0x86b62cb077ec4fd7, ; 535: System.Runtime.Serialization.Xml => 122
	i64 u0x8706ffb12bf3f53d, ; 536: Xamarin.AndroidX.Annotation.Experimental => 215
	i64 u0x872a5b14c18d328c, ; 537: System.ComponentModel.DataAnnotations => 19
	i64 u0x872fb9615bc2dff0, ; 538: Xamarin.Android.Glide.Annotations.dll => 209
	i64 u0x87c69b87d9283884, ; 539: lib_System.Threading.Thread.dll.so => 154
	i64 u0x87f6569b25707834, ; 540: System.IO.Compression.Brotli.dll => 48
	i64 u0x8842b3a5d2d3fb36, ; 541: Microsoft.Maui.Essentials => 200
	i64 u0x88926583efe7ee86, ; 542: Xamarin.AndroidX.Activity.Ktx.dll => 213
	i64 u0x88ba6bc4f7762b03, ; 543: lib_System.Reflection.dll.so => 105
	i64 u0x88bda98e0cffb7a9, ; 544: lib_Xamarin.KotlinX.Coroutines.Core.Jvm.dll.so => 301
	i64 u0x8930322c7bd8f768, ; 545: netstandard => 176
	i64 u0x897a606c9e39c75f, ; 546: lib_System.ComponentModel.Primitives.dll.so => 21
	i64 u0x898a5c6bc9e47ec1, ; 547: lib_Xamarin.AndroidX.SavedState.SavedState.Android.dll.so => 270
	i64 u0x89911a22005b92b7, ; 548: System.IO.FileSystem.DriveInfo.dll => 53
	i64 u0x89c5188089ec2cd5, ; 549: lib_System.Runtime.InteropServices.RuntimeInformation.dll.so => 114
	i64 u0x8a19e3dc71b34b2c, ; 550: System.Reflection.TypeExtensions.dll => 104
	i64 u0x8ad229ea26432ee2, ; 551: Xamarin.AndroidX.Loader => 259
	i64 u0x8b4ff5d0fdd5faa1, ; 552: lib_System.Diagnostics.DiagnosticSource.dll.so => 32
	i64 u0x8b541d476eb3774c, ; 553: System.Security.Principal.Windows => 135
	i64 u0x8b8d01333a96d0b5, ; 554: System.Diagnostics.Process.dll => 34
	i64 u0x8b9ceca7acae3451, ; 555: lib-he-Microsoft.Maui.Controls.resources.dll.so => 313
	i64 u0x8c280170f79369a5, ; 556: FoodStreetMobile.dll => 5
	i64 u0x8c575135aa1ccef4, ; 557: Microsoft.Extensions.FileProviders.Abstractions => 189
	i64 u0x8cb8f612b633affb, ; 558: Xamarin.AndroidX.SavedState.SavedState.Ktx.dll => 271
	i64 u0x8cdfdb4ce85fb925, ; 559: lib_System.Security.Principal.Windows.dll.so => 135
	i64 u0x8cdfe7b8f4caa426, ; 560: System.IO.Compression.FileSystem => 49
	i64 u0x8d0f420977c2c1c7, ; 561: Xamarin.AndroidX.CursorAdapter.dll => 233
	i64 u0x8d52f7ea2796c531, ; 562: Xamarin.AndroidX.Emoji2.dll => 238
	i64 u0x8d7b8ab4b3310ead, ; 563: System.Threading => 157
	i64 u0x8da188285aadfe8e, ; 564: System.Collections.Concurrent => 13
	i64 u0x8e8f269ad1e1ff94, ; 565: lib_Xamarin.AndroidX.Tracing.Tracing.Android.dll.so => 277
	i64 u0x8ed807bfe9858dfc, ; 566: Xamarin.AndroidX.Navigation.Common => 260
	i64 u0x8ee08b8194a30f48, ; 567: lib-hi-Microsoft.Maui.Controls.resources.dll.so => 314
	i64 u0x8ef7601039857a44, ; 568: lib-ro-Microsoft.Maui.Controls.resources.dll.so => 327
	i64 u0x8ef9414937d93a0a, ; 569: SQLitePCLRaw.core.dll => 205
	i64 u0x8efbc0801a122264, ; 570: Xamarin.GooglePlayServices.Tasks.dll => 295
	i64 u0x8f32c6f611f6ffab, ; 571: pt/Microsoft.Maui.Controls.resources.dll => 326
	i64 u0x8f44b45eb046bbd1, ; 572: System.ServiceModel.Web.dll => 139
	i64 u0x8f78f15af91fc92b, ; 573: ja/FoodStreetMobile.resources => 1
	i64 u0x8f8829d21c8985a4, ; 574: lib-pt-BR-Microsoft.Maui.Controls.resources.dll.so => 325
	i64 u0x8fbf5b0114c6dcef, ; 575: System.Globalization.dll => 47
	i64 u0x8fcc8c2a81f3d9e7, ; 576: Xamarin.KotlinX.Serialization.Core => 302
	i64 u0x8fd27d934d7b3a55, ; 577: SQLitePCLRaw.core => 205
	i64 u0x90263f8448b8f572, ; 578: lib_System.Diagnostics.TraceSource.dll.so => 38
	i64 u0x903101b46fb73a04, ; 579: _Microsoft.Android.Resource.Designer => 338
	i64 u0x90393bd4865292f3, ; 580: lib_System.IO.Compression.dll.so => 51
	i64 u0x905e2b8e7ae91ae6, ; 581: System.Threading.Tasks.Extensions.dll => 151
	i64 u0x90634f86c5ebe2b5, ; 582: Xamarin.AndroidX.Lifecycle.ViewModel.Android => 255
	i64 u0x907b636704ad79ef, ; 583: lib_Microsoft.Maui.Controls.Xaml.dll.so => 198
	i64 u0x90e9efbfd68593e0, ; 584: lib_Xamarin.AndroidX.Lifecycle.LiveData.dll.so => 246
	i64 u0x91418dc638b29e68, ; 585: lib_Xamarin.AndroidX.CustomView.dll.so => 234
	i64 u0x9157bd523cd7ed36, ; 586: lib_System.Text.Json.dll.so => 145
	i64 u0x91a74f07b30d37e2, ; 587: System.Linq.dll => 68
	i64 u0x91cb86ea3b17111d, ; 588: System.ServiceModel.Web => 139
	i64 u0x91fa41a87223399f, ; 589: ca/Microsoft.Maui.Controls.resources.dll => 305
	i64 u0x92054e486c0c7ea7, ; 590: System.IO.FileSystem.DriveInfo => 53
	i64 u0x928614058c40c4cd, ; 591: lib_System.Xml.XPath.XDocument.dll.so => 168
	i64 u0x92b138fffca2b01e, ; 592: lib_Xamarin.AndroidX.Arch.Core.Runtime.dll.so => 220
	i64 u0x92dfc2bfc6c6a888, ; 593: Xamarin.AndroidX.Lifecycle.LiveData => 246
	i64 u0x933da2c779423d68, ; 594: Xamarin.Android.Glide.Annotations => 209
	i64 u0x9388aad9b7ae40ce, ; 595: lib_Xamarin.AndroidX.Lifecycle.Common.dll.so => 244
	i64 u0x93cfa73ab28d6e35, ; 596: ms/Microsoft.Maui.Controls.resources => 321
	i64 u0x941c00d21e5c0679, ; 597: lib_Xamarin.AndroidX.Transition.dll.so => 278
	i64 u0x944077d8ca3c6580, ; 598: System.IO.Compression.dll => 51
	i64 u0x948cffedc8ed7960, ; 599: System.Xml => 172
	i64 u0x94bbeab0d4764588, ; 600: System.IO.Hashing.dll => 181
	i64 u0x94c8990839c4bdb1, ; 601: lib_Xamarin.AndroidX.Interpolator.dll.so => 243
	i64 u0x967fc325e09bfa8c, ; 602: es/Microsoft.Maui.Controls.resources => 310
	i64 u0x9686161486d34b81, ; 603: lib_Xamarin.AndroidX.ExifInterface.dll.so => 240
	i64 u0x9732d8dbddea3d9a, ; 604: id/Microsoft.Maui.Controls.resources => 317
	i64 u0x978be80e5210d31b, ; 605: Microsoft.Maui.Graphics.dll => 201
	i64 u0x979ab54025cc1c7f, ; 606: lib_Xamarin.GooglePlayServices.Base.dll.so => 292
	i64 u0x97b8c771ea3e4220, ; 607: System.ComponentModel.dll => 23
	i64 u0x97e144c9d3c6976e, ; 608: System.Collections.Concurrent.dll => 13
	i64 u0x984184e3c70d4419, ; 609: GoogleGson => 183
	i64 u0x9843944103683dd3, ; 610: Xamarin.AndroidX.Core.Core.Ktx => 231
	i64 u0x98b05cc81e6f333c, ; 611: Xamarin.AndroidX.SavedState.SavedState.Android.dll => 270
	i64 u0x98d720cc4597562c, ; 612: System.Security.Cryptography.OpenSsl => 131
	i64 u0x991d510397f92d9d, ; 613: System.Linq.Expressions => 65
	i64 u0x996ceeb8a3da3d67, ; 614: System.Threading.Overlapped.dll => 149
	i64 u0x99a00ca5270c6878, ; 615: Xamarin.AndroidX.Navigation.Runtime => 263
	i64 u0x99cdc6d1f2d3a72f, ; 616: ko/Microsoft.Maui.Controls.resources.dll => 320
	i64 u0x9a01b1da98b6ee10, ; 617: Xamarin.AndroidX.Lifecycle.Runtime.dll => 250
	i64 u0x9a5ccc274fd6e6ee, ; 618: Jsr305Binding.dll => 288
	i64 u0x9ae6940b11c02876, ; 619: lib_Xamarin.AndroidX.Window.dll.so => 284
	i64 u0x9b211a749105beac, ; 620: System.Transactions.Local => 158
	i64 u0x9b8734714671022d, ; 621: System.Threading.Tasks.Dataflow.dll => 150
	i64 u0x9bc6aea27fbf034f, ; 622: lib_Xamarin.KotlinX.Coroutines.Core.dll.so => 300
	i64 u0x9c244ac7cda32d26, ; 623: System.Security.Cryptography.X509Certificates.dll => 133
	i64 u0x9c465f280cf43733, ; 624: lib_Xamarin.KotlinX.Coroutines.Android.dll.so => 299
	i64 u0x9c8f6872beab6408, ; 625: System.Xml.XPath.XDocument.dll => 168
	i64 u0x9ce01cf91101ae23, ; 626: System.Xml.XmlDocument => 170
	i64 u0x9d128180c81d7ce6, ; 627: Xamarin.AndroidX.CustomView.PoolingContainer => 235
	i64 u0x9d5dbcf5a48583fe, ; 628: lib_Xamarin.AndroidX.Activity.dll.so => 212
	i64 u0x9d74dee1a7725f34, ; 629: Microsoft.Extensions.Configuration.Abstractions.dll => 185
	i64 u0x9dd0e195825d65c6, ; 630: lib_Xamarin.AndroidX.Navigation.Runtime.Android.dll.so => 264
	i64 u0x9e4534b6adaf6e84, ; 631: nl/Microsoft.Maui.Controls.resources => 323
	i64 u0x9e4b95dec42769f7, ; 632: System.Diagnostics.Debug.dll => 31
	i64 u0x9eaf1efdf6f7267e, ; 633: Xamarin.AndroidX.Navigation.Common.dll => 260
	i64 u0x9ef542cf1f78c506, ; 634: Xamarin.AndroidX.Lifecycle.LiveData.Core => 247
	i64 u0xa00832eb975f56a8, ; 635: lib_System.Net.dll.so => 89
	i64 u0xa09053d4f84fd0c8, ; 636: en/FoodStreetMobile.resources.dll => 0
	i64 u0xa0ad78236b7b267f, ; 637: Xamarin.AndroidX.Window => 284
	i64 u0xa0d8259f4cc284ec, ; 638: lib_System.Security.Cryptography.dll.so => 134
	i64 u0xa0e17ca50c77a225, ; 639: lib_Xamarin.Google.Crypto.Tink.Android.dll.so => 289
	i64 u0xa0ff9b3e34d92f11, ; 640: lib_System.Resources.Writer.dll.so => 108
	i64 u0xa12fbfb4da97d9f3, ; 641: System.Threading.Timer.dll => 156
	i64 u0xa1440773ee9d341e, ; 642: Xamarin.Google.Android.Material => 287
	i64 u0xa18c39c44cdc3465, ; 643: Xamarin.AndroidX.Window.WindowCore => 285
	i64 u0xa1b9d7c27f47219f, ; 644: Xamarin.AndroidX.Navigation.UI.dll => 265
	i64 u0xa2572680829d2c7c, ; 645: System.IO.Pipelines.dll => 59
	i64 u0xa26597e57ee9c7f6, ; 646: System.Xml.XmlDocument.dll => 170
	i64 u0xa308401900e5bed3, ; 647: lib_mscorlib.dll.so => 175
	i64 u0xa395572e7da6c99d, ; 648: lib_System.Security.dll.so => 138
	i64 u0xa3e683f24b43af6f, ; 649: System.Dynamic.Runtime.dll => 42
	i64 u0xa4145becdee3dc4f, ; 650: Xamarin.AndroidX.VectorDrawable.Animated => 280
	i64 u0xa46aa1eaa214539b, ; 651: ko/Microsoft.Maui.Controls.resources => 320
	i64 u0xa4edc8f2ceae241a, ; 652: System.Data.Common.dll => 27
	i64 u0xa5494f40f128ce6a, ; 653: System.Runtime.Serialization.Formatters.dll => 119
	i64 u0xa54b74df83dce92b, ; 654: System.Reflection.DispatchProxy => 97
	i64 u0xa5b7152421ed6d98, ; 655: lib_System.IO.FileSystem.Watcher.dll.so => 55
	i64 u0xa5c3844f17b822db, ; 656: lib_System.Linq.Parallel.dll.so => 66
	i64 u0xa5ce5c755bde8cb8, ; 657: lib_System.Security.Cryptography.Csp.dll.so => 129
	i64 u0xa5e599d1e0524750, ; 658: System.Numerics.Vectors.dll => 90
	i64 u0xa5f1ba49b85dd355, ; 659: System.Security.Cryptography.dll => 134
	i64 u0xa61975a5a37873ea, ; 660: lib_System.Xml.XmlSerializer.dll.so => 171
	i64 u0xa6593e21584384d2, ; 661: lib_Jsr305Binding.dll.so => 288
	i64 u0xa66cbee0130865f7, ; 662: lib_WindowsBase.dll.so => 174
	i64 u0xa67dbee13e1df9ca, ; 663: Xamarin.AndroidX.SavedState.dll => 269
	i64 u0xa684b098dd27b296, ; 664: lib_Xamarin.AndroidX.Security.SecurityCrypto.dll.so => 272
	i64 u0xa68a420042bb9b1f, ; 665: Xamarin.AndroidX.DrawerLayout.dll => 236
	i64 u0xa6d26156d1cacc7c, ; 666: Xamarin.Android.Glide.dll => 208
	i64 u0xa75386b5cb9595aa, ; 667: Xamarin.AndroidX.Lifecycle.Runtime.Android => 251
	i64 u0xa763fbb98df8d9fb, ; 668: lib_Microsoft.Win32.Primitives.dll.so => 9
	i64 u0xa78ce3745383236a, ; 669: Xamarin.AndroidX.Lifecycle.Common.Jvm => 245
	i64 u0xa7c31b56b4dc7b33, ; 670: hu/Microsoft.Maui.Controls.resources => 316
	i64 u0xa7eab29ed44b4e7a, ; 671: Mono.Android.Export => 178
	i64 u0xa8195217cbf017b7, ; 672: Microsoft.VisualBasic.Core => 7
	i64 u0xa843f6095f0d247d, ; 673: Xamarin.GooglePlayServices.Base.dll => 292
	i64 u0xa859a95830f367ff, ; 674: lib_Xamarin.AndroidX.Lifecycle.ViewModel.Ktx.dll.so => 256
	i64 u0xa8b52f21e0dbe690, ; 675: System.Runtime.Serialization.dll => 123
	i64 u0xa8ee4ed7de2efaee, ; 676: Xamarin.AndroidX.Annotation.dll => 214
	i64 u0xa95590e7c57438a4, ; 677: System.Configuration => 24
	i64 u0xaa2219c8e3449ff5, ; 678: Microsoft.Extensions.Logging.Abstractions => 192
	i64 u0xaa443ac34067eeef, ; 679: System.Private.Xml.dll => 96
	i64 u0xaa52de307ef5d1dd, ; 680: System.Net.Http => 71
	i64 u0xaa9a7b0214a5cc5c, ; 681: System.Diagnostics.StackTrace.dll => 35
	i64 u0xaaaf86367285a918, ; 682: Microsoft.Extensions.DependencyInjection.Abstractions.dll => 187
	i64 u0xaaf84bb3f052a265, ; 683: el/Microsoft.Maui.Controls.resources => 309
	i64 u0xab9af77b5b67a0b8, ; 684: Xamarin.AndroidX.ConstraintLayout.Core => 228
	i64 u0xab9c1b2687d86b0b, ; 685: lib_System.Linq.Expressions.dll.so => 65
	i64 u0xac2af3fa195a15ce, ; 686: System.Runtime.Numerics => 118
	i64 u0xac5376a2a538dc10, ; 687: Xamarin.AndroidX.Lifecycle.LiveData.Core.dll => 247
	i64 u0xac5acae88f60357e, ; 688: System.Diagnostics.Tools.dll => 37
	i64 u0xac79c7e46047ad98, ; 689: System.Security.Principal.Windows.dll => 135
	i64 u0xac98d31068e24591, ; 690: System.Xml.XDocument => 167
	i64 u0xacd46e002c3ccb97, ; 691: ro/Microsoft.Maui.Controls.resources => 327
	i64 u0xacdd9e4180d56dda, ; 692: Xamarin.AndroidX.Concurrent.Futures => 226
	i64 u0xacf42eea7ef9cd12, ; 693: System.Threading.Channels => 148
	i64 u0xad387902a3200e8d, ; 694: ja/FoodStreetMobile.resources.dll => 1
	i64 u0xad89c07347f1bad6, ; 695: nl/Microsoft.Maui.Controls.resources.dll => 323
	i64 u0xadbb53caf78a79d2, ; 696: System.Web.HttpUtility => 161
	i64 u0xadc90ab061a9e6e4, ; 697: System.ComponentModel.TypeConverter.dll => 22
	i64 u0xadca1b9030b9317e, ; 698: Xamarin.AndroidX.Collection.Ktx => 225
	i64 u0xadd8eda2edf396ad, ; 699: Xamarin.Android.Glide.GifDecoder => 211
	i64 u0xadf4cf30debbeb9a, ; 700: System.Net.ServicePoint.dll => 82
	i64 u0xadf511667bef3595, ; 701: System.Net.Security => 80
	i64 u0xae0aaa94fdcfce0f, ; 702: System.ComponentModel.EventBasedAsync.dll => 20
	i64 u0xae282bcd03739de7, ; 703: Java.Interop => 177
	i64 u0xae53579c90db1107, ; 704: System.ObjectModel.dll => 92
	i64 u0xae7ea18c61eef394, ; 705: SQLite-net => 203
	i64 u0xaf732d0b2193b8f5, ; 706: System.Security.Cryptography.OpenSsl.dll => 131
	i64 u0xafdb94dbccd9d11c, ; 707: Xamarin.AndroidX.Lifecycle.LiveData.dll => 246
	i64 u0xafe29f45095518e7, ; 708: lib_Xamarin.AndroidX.Lifecycle.ViewModelSavedState.dll.so => 257
	i64 u0xb03ae931fb25607e, ; 709: Xamarin.AndroidX.ConstraintLayout => 227
	i64 u0xb05cc42cd94c6d9d, ; 710: lib-sv-Microsoft.Maui.Controls.resources.dll.so => 330
	i64 u0xb0ac21bec8f428c5, ; 711: Xamarin.AndroidX.Lifecycle.Runtime.Ktx.Android.dll => 253
	i64 u0xb0bb43dc52ea59f9, ; 712: System.Diagnostics.Tracing.dll => 39
	i64 u0xb1dd05401aa8ee63, ; 713: System.Security.AccessControl => 125
	i64 u0xb220631954820169, ; 714: System.Text.RegularExpressions => 146
	i64 u0xb2376e1dbf8b4ed7, ; 715: System.Security.Cryptography.Csp => 129
	i64 u0xb2a1959fe95c5402, ; 716: lib_System.Runtime.InteropServices.JavaScript.dll.so => 113
	i64 u0xb2a3f67f3bf29fce, ; 717: da/Microsoft.Maui.Controls.resources => 307
	i64 u0xb3874072ee0ecf8c, ; 718: Xamarin.AndroidX.VectorDrawable.Animated.dll => 280
	i64 u0xb3f0a0fcda8d3ebc, ; 719: Xamarin.AndroidX.CardView => 222
	i64 u0xb46be1aa6d4fff93, ; 720: hi/Microsoft.Maui.Controls.resources => 314
	i64 u0xb477491be13109d8, ; 721: ar/Microsoft.Maui.Controls.resources => 304
	i64 u0xb4bd7015ecee9d86, ; 722: System.IO.Pipelines => 59
	i64 u0xb4c53d9749c5f226, ; 723: lib_System.IO.FileSystem.AccessControl.dll.so => 52
	i64 u0xb4ff710863453fda, ; 724: System.Diagnostics.FileVersionInfo.dll => 33
	i64 u0xb54092076b15e062, ; 725: System.Threading.AccessControl => 147
	i64 u0xb5c38bf497a4cfe2, ; 726: lib_System.Threading.Tasks.dll.so => 153
	i64 u0xb5c7fcdafbc67ee4, ; 727: Microsoft.Extensions.Logging.Abstractions.dll => 192
	i64 u0xb5ea31d5244c6626, ; 728: System.Threading.ThreadPool.dll => 155
	i64 u0xb7212c4683a94afe, ; 729: System.Drawing.Primitives => 40
	i64 u0xb7b7753d1f319409, ; 730: sv/Microsoft.Maui.Controls.resources => 330
	i64 u0xb81a2c6e0aee50fe, ; 731: lib_System.Private.CoreLib.dll.so => 182
	i64 u0xb8c60af47c08d4da, ; 732: System.Net.ServicePoint => 82
	i64 u0xb8e68d20aad91196, ; 733: lib_System.Xml.XPath.dll.so => 169
	i64 u0xb9185c33a1643eed, ; 734: Microsoft.CSharp.dll => 6
	i64 u0xb960d6b2200ba320, ; 735: Xamarin.AndroidX.Lifecycle.ViewModelSavedState.Android.dll => 258
	i64 u0xb9b19a3eb1924681, ; 736: lib_Microsoft.Maui.Controls.Maps.dll.so => 197
	i64 u0xb9b8001adf4ed7cc, ; 737: lib_Xamarin.AndroidX.SlidingPaneLayout.dll.so => 273
	i64 u0xb9f64d3b230def68, ; 738: lib-pt-Microsoft.Maui.Controls.resources.dll.so => 326
	i64 u0xb9fc3c8a556e3691, ; 739: ja/Microsoft.Maui.Controls.resources => 319
	i64 u0xba4670aa94a2b3c6, ; 740: lib_System.Xml.XDocument.dll.so => 167
	i64 u0xba48785529705af9, ; 741: System.Collections.dll => 17
	i64 u0xba965b8c86359996, ; 742: lib_System.Windows.dll.so => 163
	i64 u0xbb286883bc35db36, ; 743: System.Transactions.dll => 159
	i64 u0xbb65706fde942ce3, ; 744: System.Net.Sockets => 83
	i64 u0xbba28979413cad9e, ; 745: lib_System.Runtime.CompilerServices.VisualC.dll.so => 110
	i64 u0xbbd180354b67271a, ; 746: System.Runtime.Serialization.Formatters => 119
	i64 u0xbc22a245dab70cb4, ; 747: lib_SQLitePCLRaw.provider.e_sqlite3.dll.so => 207
	i64 u0xbc260cdba33291a3, ; 748: Xamarin.AndroidX.Arch.Core.Common.dll => 219
	i64 u0xbd0e2c0d55246576, ; 749: System.Net.Http.dll => 71
	i64 u0xbd3fbd85b9e1cb29, ; 750: lib_System.Net.HttpListener.dll.so => 72
	i64 u0xbd437a2cdb333d0d, ; 751: Xamarin.AndroidX.ViewPager2 => 283
	i64 u0xbd4f572d2bd0a789, ; 752: System.IO.Compression.ZipFile.dll => 50
	i64 u0xbd5d0b88d3d647a5, ; 753: lib_Xamarin.AndroidX.Browser.dll.so => 221
	i64 u0xbd877b14d0b56392, ; 754: System.Runtime.Intrinsics.dll => 116
	i64 u0xbe65a49036345cf4, ; 755: lib_System.Buffers.dll.so => 12
	i64 u0xbee38d4a88835966, ; 756: Xamarin.AndroidX.AppCompat.AppCompatResources => 218
	i64 u0xbef9919db45b4ca7, ; 757: System.IO.Pipes.AccessControl => 60
	i64 u0xbf0fa68611139208, ; 758: lib_Xamarin.AndroidX.Annotation.dll.so => 214
	i64 u0xbfc1e1fb3095f2b3, ; 759: lib_System.Net.Http.Json.dll.so => 70
	i64 u0xc040a4ab55817f58, ; 760: ar/Microsoft.Maui.Controls.resources.dll => 304
	i64 u0xc07cadab29efeba0, ; 761: Xamarin.AndroidX.Core.Core.Ktx.dll => 231
	i64 u0xc0d928351ab5ca77, ; 762: System.Console.dll => 25
	i64 u0xc0f5a221a9383aea, ; 763: System.Runtime.Intrinsics => 116
	i64 u0xc111030af54d7191, ; 764: System.Resources.Writer => 108
	i64 u0xc12b8b3afa48329c, ; 765: lib_System.Linq.dll.so => 68
	i64 u0xc12ccf7367fc063b, ; 766: lib_FoodStreetMobile.dll.so => 5
	i64 u0xc183ca0b74453aa9, ; 767: lib_System.Threading.Tasks.Dataflow.dll.so => 150
	i64 u0xc1ff9ae3cdb6e1e6, ; 768: Xamarin.AndroidX.Activity.dll => 212
	i64 u0xc26c064effb1dea9, ; 769: System.Buffers.dll => 12
	i64 u0xc28c50f32f81cc73, ; 770: ja/Microsoft.Maui.Controls.resources.dll => 319
	i64 u0xc2902f6cf5452577, ; 771: lib_Mono.Android.Export.dll.so => 178
	i64 u0xc2a3bca55b573141, ; 772: System.IO.FileSystem.Watcher => 55
	i64 u0xc2bcfec99f69365e, ; 773: Xamarin.AndroidX.ViewPager2.dll => 283
	i64 u0xc30b52815b58ac2c, ; 774: lib_System.Runtime.Serialization.Xml.dll.so => 122
	i64 u0xc36d7d89c652f455, ; 775: System.Threading.Overlapped => 149
	i64 u0xc396b285e59e5493, ; 776: GoogleGson.dll => 183
	i64 u0xc3c86c1e5e12f03d, ; 777: WindowsBase => 174
	i64 u0xc421b61fd853169d, ; 778: lib_System.Net.WebSockets.Client.dll.so => 87
	i64 u0xc463e077917aa21d, ; 779: System.Runtime.Serialization.Json => 120
	i64 u0xc4d3858ed4d08512, ; 780: Xamarin.AndroidX.Lifecycle.ViewModelSavedState.dll => 257
	i64 u0xc50fded0ded1418c, ; 781: lib_System.ComponentModel.TypeConverter.dll.so => 22
	i64 u0xc519125d6bc8fb11, ; 782: lib_System.Net.Requests.dll.so => 79
	i64 u0xc5293b19e4dc230e, ; 783: Xamarin.AndroidX.Navigation.Fragment => 262
	i64 u0xc5325b2fcb37446f, ; 784: lib_System.Private.Xml.dll.so => 96
	i64 u0xc535cb9a21385d9b, ; 785: lib_Xamarin.Android.Glide.DiskLruCache.dll.so => 210
	i64 u0xc5a0f4b95a699af7, ; 786: lib_System.Private.Uri.dll.so => 94
	i64 u0xc5cdcd5b6277579e, ; 787: lib_System.Security.Cryptography.Algorithms.dll.so => 127
	i64 u0xc5ec286825cb0bf4, ; 788: Xamarin.AndroidX.Tracing.Tracing => 276
	i64 u0xc5f593bc0fb62095, ; 789: lib-ru-FoodStreetMobile.resources.dll.so => 3
	i64 u0xc64f6952cef5d09f, ; 790: Microsoft.Maui.Maps.dll => 202
	i64 u0xc6706bc8aa7fe265, ; 791: Xamarin.AndroidX.Annotation.Jvm => 216
	i64 u0xc68e480c8069e1f7, ; 792: Microsoft.Maui.Maps => 202
	i64 u0xc74d70d4aa96cef3, ; 793: Xamarin.AndroidX.Navigation.Runtime.Android => 264
	i64 u0xc7c01e7d7c93a110, ; 794: System.Text.Encoding.Extensions.dll => 142
	i64 u0xc7ce851898a4548e, ; 795: lib_System.Web.HttpUtility.dll.so => 161
	i64 u0xc809d4089d2556b2, ; 796: System.Runtime.InteropServices.JavaScript.dll => 113
	i64 u0xc858a28d9ee5a6c5, ; 797: lib_System.Collections.Specialized.dll.so => 16
	i64 u0xc8ac7c6bf1c2ec51, ; 798: System.Reflection.DispatchProxy.dll => 97
	i64 u0xc9c62c8f354ac568, ; 799: lib_System.Diagnostics.TextWriterTraceListener.dll.so => 36
	i64 u0xca3a723e7342c5b6, ; 800: lib-tr-Microsoft.Maui.Controls.resources.dll.so => 332
	i64 u0xca5801070d9fccfb, ; 801: System.Text.Encoding => 143
	i64 u0xcab3493c70141c2d, ; 802: pl/Microsoft.Maui.Controls.resources => 324
	i64 u0xcacfddc9f7c6de76, ; 803: ro/Microsoft.Maui.Controls.resources.dll => 327
	i64 u0xcadbc92899a777f0, ; 804: Xamarin.AndroidX.Startup.StartupRuntime => 274
	i64 u0xcba1cb79f45292b5, ; 805: Xamarin.Android.Glide.GifDecoder.dll => 211
	i64 u0xcbb5f80c7293e696, ; 806: lib_System.Globalization.Calendars.dll.so => 45
	i64 u0xcbd4fdd9cef4a294, ; 807: lib__Microsoft.Android.Resource.Designer.dll.so => 338
	i64 u0xcc15da1e07bbd994, ; 808: Xamarin.AndroidX.SlidingPaneLayout => 273
	i64 u0xcc2876b32ef2794c, ; 809: lib_System.Text.RegularExpressions.dll.so => 146
	i64 u0xcc5c3bb714c4561e, ; 810: Xamarin.KotlinX.Coroutines.Core.Jvm.dll => 301
	i64 u0xcc76886e09b88260, ; 811: Xamarin.KotlinX.Serialization.Core.Jvm.dll => 303
	i64 u0xcc9fa2923aa1c9ef, ; 812: System.Diagnostics.Contracts.dll => 30
	i64 u0xccae9bb73e2326bd, ; 813: lib_System.IO.Hashing.dll.so => 181
	i64 u0xccf25c4b634ccd3a, ; 814: zh-Hans/Microsoft.Maui.Controls.resources.dll => 336
	i64 u0xcd10a42808629144, ; 815: System.Net.Requests => 79
	i64 u0xcdca1b920e9f53ba, ; 816: Xamarin.AndroidX.Interpolator => 243
	i64 u0xcdd0c48b6937b21c, ; 817: Xamarin.AndroidX.SwipeRefreshLayout => 275
	i64 u0xcf23d8093f3ceadf, ; 818: System.Diagnostics.DiagnosticSource.dll => 32
	i64 u0xcf5ff6b6b2c4c382, ; 819: System.Net.Mail.dll => 73
	i64 u0xcf8fc898f98b0d34, ; 820: System.Private.Xml.Linq => 95
	i64 u0xcfb21487d9cb358b, ; 821: Xamarin.GooglePlayServices.Maps.dll => 294
	i64 u0xd04b5f59ed596e31, ; 822: System.Reflection.Metadata.dll => 102
	i64 u0xd063299fcfc0c93f, ; 823: lib_System.Runtime.Serialization.Json.dll.so => 120
	i64 u0xd0de8a113e976700, ; 824: System.Diagnostics.TextWriterTraceListener => 36
	i64 u0xd0fc33d5ae5d4cb8, ; 825: System.Runtime.Extensions => 111
	i64 u0xd1194e1d8a8de83c, ; 826: lib_Xamarin.AndroidX.Lifecycle.Common.Jvm.dll.so => 245
	i64 u0xd12beacdfc14f696, ; 827: System.Dynamic.Runtime => 42
	i64 u0xd16fd7fb9bbcd43e, ; 828: Microsoft.Extensions.Diagnostics.Abstractions => 188
	i64 u0xd198e7ce1b6a8344, ; 829: System.Net.Quic.dll => 78
	i64 u0xd3144156a3727ebe, ; 830: Xamarin.Google.Guava.ListenableFuture => 291
	i64 u0xd333d0af9e423810, ; 831: System.Runtime.InteropServices => 115
	i64 u0xd33a415cb4278969, ; 832: System.Security.Cryptography.Encoding.dll => 130
	i64 u0xd3426d966bb704f5, ; 833: Xamarin.AndroidX.AppCompat.AppCompatResources.dll => 218
	i64 u0xd3651b6fc3125825, ; 834: System.Private.Uri.dll => 94
	i64 u0xd373685349b1fe8b, ; 835: Microsoft.Extensions.Logging.dll => 191
	i64 u0xd3801faafafb7698, ; 836: System.Private.DataContractSerialization.dll => 93
	i64 u0xd3e4c8d6a2d5d470, ; 837: it/Microsoft.Maui.Controls.resources => 318
	i64 u0xd3edcc1f25459a50, ; 838: System.Reflection.Emit => 100
	i64 u0xd4645626dffec99d, ; 839: lib_Microsoft.Extensions.DependencyInjection.Abstractions.dll.so => 187
	i64 u0xd4fa0abb79079ea9, ; 840: System.Security.Principal.dll => 136
	i64 u0xd5507e11a2b2839f, ; 841: Xamarin.AndroidX.Lifecycle.ViewModelSavedState => 257
	i64 u0xd5d04bef8478ea19, ; 842: Xamarin.AndroidX.Tracing.Tracing.dll => 276
	i64 u0xd60815f26a12e140, ; 843: Microsoft.Extensions.Logging.Debug.dll => 193
	i64 u0xd6694f8359737e4e, ; 844: Xamarin.AndroidX.SavedState => 269
	i64 u0xd6949e129339eae5, ; 845: lib_Xamarin.AndroidX.Core.Core.Ktx.dll.so => 231
	i64 u0xd6d21782156bc35b, ; 846: Xamarin.AndroidX.SwipeRefreshLayout.dll => 275
	i64 u0xd6de019f6af72435, ; 847: Xamarin.AndroidX.ConstraintLayout.Core.dll => 228
	i64 u0xd70956d1e6deefb9, ; 848: Jsr305Binding => 288
	i64 u0xd72329819cbbbc44, ; 849: lib_Microsoft.Extensions.Configuration.Abstractions.dll.so => 185
	i64 u0xd72c760af136e863, ; 850: System.Xml.XmlSerializer.dll => 171
	i64 u0xd753f071e44c2a03, ; 851: lib_System.Security.SecureString.dll.so => 137
	i64 u0xd7b3764ada9d341d, ; 852: lib_Microsoft.Extensions.Logging.Abstractions.dll.so => 192
	i64 u0xd7f0088bc5ad71f2, ; 853: Xamarin.AndroidX.VersionedParcelable => 281
	i64 u0xd824ef6ab33f8f7a, ; 854: Xamarin.AndroidX.Window.WindowCore.dll => 285
	i64 u0xd8fb25e28ae30a12, ; 855: Xamarin.AndroidX.ProfileInstaller.ProfileInstaller.dll => 266
	i64 u0xda1dfa4c534a9251, ; 856: Microsoft.Extensions.DependencyInjection => 186
	i64 u0xdad05a11827959a3, ; 857: System.Collections.NonGeneric.dll => 15
	i64 u0xdaefdfe71aa53cf9, ; 858: System.IO.FileSystem.Primitives => 54
	i64 u0xdb5383ab5865c007, ; 859: lib-vi-Microsoft.Maui.Controls.resources.dll.so => 334
	i64 u0xdb58816721c02a59, ; 860: lib_System.Reflection.Emit.ILGeneration.dll.so => 98
	i64 u0xdbeda89f832aa805, ; 861: vi/Microsoft.Maui.Controls.resources.dll => 334
	i64 u0xdbf2a779fbc3ac31, ; 862: System.Transactions.Local.dll => 158
	i64 u0xdbf9607a441b4505, ; 863: System.Linq => 68
	i64 u0xdbfc90157a0de9b0, ; 864: lib_System.Text.Encoding.dll.so => 143
	i64 u0xdc75032002d1a212, ; 865: lib_System.Transactions.Local.dll.so => 158
	i64 u0xdca8be7403f92d4f, ; 866: lib_System.Linq.Queryable.dll.so => 67
	i64 u0xdce2c53525640bf3, ; 867: Microsoft.Extensions.Logging => 191
	i64 u0xdd2b722d78ef5f43, ; 868: System.Runtime.dll => 124
	i64 u0xdd67031857c72f96, ; 869: lib_System.Text.Encodings.Web.dll.so => 144
	i64 u0xdd70765ad6162057, ; 870: Xamarin.JSpecify => 297
	i64 u0xdd92e229ad292030, ; 871: System.Numerics.dll => 91
	i64 u0xdde30e6b77aa6f6c, ; 872: lib-zh-Hans-Microsoft.Maui.Controls.resources.dll.so => 336
	i64 u0xddf15bf070284fc4, ; 873: ko/FoodStreetMobile.resources => 2
	i64 u0xde110ae80fa7c2e2, ; 874: System.Xml.XDocument.dll => 167
	i64 u0xde4726fcdf63a198, ; 875: Xamarin.AndroidX.Transition => 278
	i64 u0xde572c2b2fb32f93, ; 876: lib_System.Threading.Tasks.Extensions.dll.so => 151
	i64 u0xde8769ebda7d8647, ; 877: hr/Microsoft.Maui.Controls.resources.dll => 315
	i64 u0xdee075f3477ef6be, ; 878: Xamarin.AndroidX.ExifInterface.dll => 240
	i64 u0xdf4b773de8fb1540, ; 879: System.Net.dll => 89
	i64 u0xdf9c7682560a9629, ; 880: System.Net.ServerSentEvents => 81
	i64 u0xdfa254ebb4346068, ; 881: System.Net.Ping => 76
	i64 u0xe0142572c095a480, ; 882: Xamarin.AndroidX.AppCompat.dll => 217
	i64 u0xe021eaa401792a05, ; 883: System.Text.Encoding.dll => 143
	i64 u0xe02f89350ec78051, ; 884: Xamarin.AndroidX.CoordinatorLayout.dll => 229
	i64 u0xe0496b9d65ef5474, ; 885: Xamarin.Android.Glide.DiskLruCache.dll => 210
	i64 u0xe10b760bb1462e7a, ; 886: lib_System.Security.Cryptography.Primitives.dll.so => 132
	i64 u0xe192a588d4410686, ; 887: lib_System.IO.Pipelines.dll.so => 59
	i64 u0xe1a08bd3fa539e0d, ; 888: System.Runtime.Loader => 117
	i64 u0xe1a77eb8831f7741, ; 889: System.Security.SecureString.dll => 137
	i64 u0xe1b52f9f816c70ef, ; 890: System.Private.Xml.Linq.dll => 95
	i64 u0xe1e199c8ab02e356, ; 891: System.Data.DataSetExtensions.dll => 28
	i64 u0xe1e624d740617d34, ; 892: lib-ko-FoodStreetMobile.resources.dll.so => 2
	i64 u0xe1ecfdb7fff86067, ; 893: System.Net.Security.dll => 80
	i64 u0xe2252a80fe853de4, ; 894: lib_System.Security.Principal.dll.so => 136
	i64 u0xe22fa4c9c645db62, ; 895: System.Diagnostics.TextWriterTraceListener.dll => 36
	i64 u0xe24095a7afddaab3, ; 896: lib_Microsoft.Extensions.Hosting.Abstractions.dll.so => 190
	i64 u0xe2420585aeceb728, ; 897: System.Net.Requests.dll => 79
	i64 u0xe26692647e6bcb62, ; 898: Xamarin.AndroidX.Lifecycle.Runtime.Ktx => 252
	i64 u0xe29b73bc11392966, ; 899: lib-id-Microsoft.Maui.Controls.resources.dll.so => 317
	i64 u0xe2ad448dee50fbdf, ; 900: System.Xml.Serialization => 166
	i64 u0xe2d920f978f5d85c, ; 901: System.Data.DataSetExtensions => 28
	i64 u0xe2e426c7714fa0bc, ; 902: Microsoft.Win32.Primitives.dll => 9
	i64 u0xe332bacb3eb4a806, ; 903: Mono.Android.Export.dll => 178
	i64 u0xe3811d68d4fe8463, ; 904: pt-BR/Microsoft.Maui.Controls.resources.dll => 325
	i64 u0xe3a586956771a0ed, ; 905: lib_SQLite-net.dll.so => 203
	i64 u0xe3b7cbae5ad66c75, ; 906: lib_System.Security.Cryptography.Encoding.dll.so => 130
	i64 u0xe4292b48f3224d5b, ; 907: lib_Xamarin.AndroidX.Core.ViewTree.dll.so => 232
	i64 u0xe494f7ced4ecd10a, ; 908: hu/Microsoft.Maui.Controls.resources.dll => 316
	i64 u0xe4a9b1e40d1e8917, ; 909: lib-fi-Microsoft.Maui.Controls.resources.dll.so => 311
	i64 u0xe4f74a0b5bf9703f, ; 910: System.Runtime.Serialization.Primitives => 121
	i64 u0xe5434e8a119ceb69, ; 911: lib_Mono.Android.dll.so => 180
	i64 u0xe55703b9ce5c038a, ; 912: System.Diagnostics.Tools => 37
	i64 u0xe57013c8afc270b5, ; 913: Microsoft.VisualBasic => 8
	i64 u0xe62913cc36bc07ec, ; 914: System.Xml.dll => 172
	i64 u0xe7bea09c4900a191, ; 915: Xamarin.AndroidX.VectorDrawable.dll => 279
	i64 u0xe7e03cc18dcdeb49, ; 916: lib_System.Diagnostics.StackTrace.dll.so => 35
	i64 u0xe7e147ff99a7a380, ; 917: lib_System.Configuration.dll.so => 24
	i64 u0xe86b0df4ba9e5db8, ; 918: lib_Xamarin.AndroidX.Lifecycle.Runtime.Android.dll.so => 251
	i64 u0xe896622fe0902957, ; 919: System.Reflection.Emit.dll => 100
	i64 u0xe89a2a9ef110899b, ; 920: System.Drawing.dll => 41
	i64 u0xe8c5f8c100b5934b, ; 921: Microsoft.Win32.Registry => 10
	i64 u0xe98163eb702ae5c5, ; 922: Xamarin.AndroidX.Arch.Core.Runtime => 220
	i64 u0xe994f23ba4c143e5, ; 923: Xamarin.KotlinX.Coroutines.Android => 299
	i64 u0xe9b9c8c0458fd92a, ; 924: System.Windows => 163
	i64 u0xe9d166d87a7f2bdb, ; 925: lib_Xamarin.AndroidX.Startup.StartupRuntime.dll.so => 274
	i64 u0xea5a4efc2ad81d1b, ; 926: Xamarin.Google.ErrorProne.Annotations => 290
	i64 u0xeb2313fe9d65b785, ; 927: Xamarin.AndroidX.ConstraintLayout.dll => 227
	i64 u0xed19c616b3fcb7eb, ; 928: Xamarin.AndroidX.VersionedParcelable.dll => 281
	i64 u0xedc4817167106c23, ; 929: System.Net.Sockets.dll => 83
	i64 u0xedc632067fb20ff3, ; 930: System.Memory.dll => 69
	i64 u0xedc8e4ca71a02a8b, ; 931: Xamarin.AndroidX.Navigation.Runtime.dll => 263
	i64 u0xee27c952ed6d058b, ; 932: Microsoft.Maui.Controls.Maps => 197
	i64 u0xee81f5b3f1c4f83b, ; 933: System.Threading.ThreadPool => 155
	i64 u0xeeb7ebb80150501b, ; 934: lib_Xamarin.AndroidX.Collection.Jvm.dll.so => 224
	i64 u0xeefc635595ef57f0, ; 935: System.Security.Cryptography.Cng => 128
	i64 u0xef03b1b5a04e9709, ; 936: System.Text.Encoding.CodePages.dll => 141
	i64 u0xef5bcbe61622ee5f, ; 937: Xamarin.AndroidX.Tracing.Tracing.Android.dll => 277
	i64 u0xef602c523fe2e87a, ; 938: lib_Xamarin.Google.Guava.ListenableFuture.dll.so => 291
	i64 u0xef72742e1bcca27a, ; 939: Microsoft.Maui.Essentials.dll => 200
	i64 u0xefd1e0c4e5c9b371, ; 940: System.Resources.ResourceManager.dll => 107
	i64 u0xefe8f8d5ed3c72ea, ; 941: System.Formats.Tar.dll => 44
	i64 u0xefec0b7fdc57ec42, ; 942: Xamarin.AndroidX.Activity => 212
	i64 u0xeff59cbde4363ec3, ; 943: System.Threading.AccessControl.dll => 147
	i64 u0xf00c29406ea45e19, ; 944: es/Microsoft.Maui.Controls.resources.dll => 310
	i64 u0xf09e47b6ae914f6e, ; 945: System.Net.NameResolution => 74
	i64 u0xf0ac2b489fed2e35, ; 946: lib_System.Diagnostics.Debug.dll.so => 31
	i64 u0xf0bb49dadd3a1fe1, ; 947: lib_System.Net.ServicePoint.dll.so => 82
	i64 u0xf0c16dff90fbf5d6, ; 948: Xamarin.AndroidX.Window.WindowCore.Jvm => 286
	i64 u0xf0de2537ee19c6ca, ; 949: lib_System.Net.WebHeaderCollection.dll.so => 85
	i64 u0xf1138779fa181c68, ; 950: lib_Xamarin.AndroidX.Lifecycle.Runtime.dll.so => 250
	i64 u0xf11b621fc87b983f, ; 951: Microsoft.Maui.Controls.Xaml.dll => 198
	i64 u0xf161f4f3c3b7e62c, ; 952: System.Data => 29
	i64 u0xf16eb650d5a464bc, ; 953: System.ValueTuple => 160
	i64 u0xf1c4b4005493d871, ; 954: System.Formats.Asn1.dll => 43
	i64 u0xf22514cfad2d598b, ; 955: lib_Xamarin.AndroidX.Lifecycle.ViewModelSavedState.Android.dll.so => 258
	i64 u0xf238bd79489d3a96, ; 956: lib-nl-Microsoft.Maui.Controls.resources.dll.so => 323
	i64 u0xf2feea356ba760af, ; 957: Xamarin.AndroidX.Arch.Core.Runtime.dll => 220
	i64 u0xf300e085f8acd238, ; 958: lib_System.ServiceProcess.dll.so => 140
	i64 u0xf33fa4d4847656fd, ; 959: lib-en-FoodStreetMobile.resources.dll.so => 0
	i64 u0xf34e52b26e7e059d, ; 960: System.Runtime.CompilerServices.VisualC.dll => 110
	i64 u0xf37221fda4ef8830, ; 961: lib_Xamarin.Google.Android.Material.dll.so => 287
	i64 u0xf3ad9b8fb3eefd12, ; 962: lib_System.IO.UnmanagedMemoryStream.dll.so => 62
	i64 u0xf3ddfe05336abf29, ; 963: System => 173
	i64 u0xf408654b2a135055, ; 964: System.Reflection.Emit.ILGeneration.dll => 98
	i64 u0xf4103170a1de5bd0, ; 965: System.Linq.Queryable.dll => 67
	i64 u0xf42d20c23173d77c, ; 966: lib_System.ServiceModel.Web.dll.so => 139
	i64 u0xf4c1dd70a5496a17, ; 967: System.IO.Compression => 51
	i64 u0xf4ecf4b9afc64781, ; 968: System.ServiceProcess.dll => 140
	i64 u0xf4eeeaa566e9b970, ; 969: lib_Xamarin.AndroidX.CustomView.PoolingContainer.dll.so => 235
	i64 u0xf518f63ead11fcd1, ; 970: System.Threading.Tasks => 153
	i64 u0xf5fc7602fe27b333, ; 971: System.Net.WebHeaderCollection => 85
	i64 u0xf6077741019d7428, ; 972: Xamarin.AndroidX.CoordinatorLayout => 229
	i64 u0xf6742cbf457c450b, ; 973: Xamarin.AndroidX.Lifecycle.Runtime.Android.dll => 251
	i64 u0xf6e8de2aebcbb422, ; 974: lib_Xamarin.AndroidX.Window.WindowCore.Jvm.dll.so => 286
	i64 u0xf70c0a7bf8ccf5af, ; 975: System.Web => 162
	i64 u0xf77b20923f07c667, ; 976: de/Microsoft.Maui.Controls.resources.dll => 308
	i64 u0xf7e2cac4c45067b3, ; 977: lib_System.Numerics.Vectors.dll.so => 90
	i64 u0xf7e74930e0e3d214, ; 978: zh-HK/Microsoft.Maui.Controls.resources.dll => 335
	i64 u0xf84773b5c81e3cef, ; 979: lib-uk-Microsoft.Maui.Controls.resources.dll.so => 333
	i64 u0xf8aac5ea82de1348, ; 980: System.Linq.Queryable => 67
	i64 u0xf8b77539b362d3ba, ; 981: lib_System.Reflection.Primitives.dll.so => 103
	i64 u0xf8e045dc345b2ea3, ; 982: lib_Xamarin.AndroidX.RecyclerView.dll.so => 267
	i64 u0xf915dc29808193a1, ; 983: System.Web.HttpUtility.dll => 161
	i64 u0xf96c777a2a0686f4, ; 984: hi/Microsoft.Maui.Controls.resources.dll => 314
	i64 u0xf9be54c8bcf8ff3b, ; 985: System.Security.AccessControl.dll => 125
	i64 u0xf9eec5bb3a6aedc6, ; 986: Microsoft.Extensions.Options => 194
	i64 u0xfa0e82300e67f913, ; 987: lib_System.AppContext.dll.so => 11
	i64 u0xfa2fdb27e8a2c8e8, ; 988: System.ComponentModel.EventBasedAsync => 20
	i64 u0xfa3f278f288b0e84, ; 989: lib_System.Net.Security.dll.so => 80
	i64 u0xfa504dfa0f097d72, ; 990: Microsoft.Extensions.FileProviders.Abstractions.dll => 189
	i64 u0xfa5ed7226d978949, ; 991: lib-ar-Microsoft.Maui.Controls.resources.dll.so => 304
	i64 u0xfa645d91e9fc4cba, ; 992: System.Threading.Thread => 154
	i64 u0xfad4d2c770e827f9, ; 993: lib_System.IO.IsolatedStorage.dll.so => 57
	i64 u0xfb022853d73b7fa5, ; 994: lib_SQLitePCLRaw.batteries_v2.dll.so => 204
	i64 u0xfb06dd2338e6f7c4, ; 995: System.Net.Ping.dll => 76
	i64 u0xfb087abe5365e3b7, ; 996: lib_System.Data.DataSetExtensions.dll.so => 28
	i64 u0xfb846e949baff5ea, ; 997: System.Xml.Serialization.dll => 166
	i64 u0xfbad3e4ce4b98145, ; 998: System.Security.Cryptography.X509Certificates => 133
	i64 u0xfbf0a31c9fc34bc4, ; 999: lib_System.Net.Http.dll.so => 71
	i64 u0xfc6b7527cc280b3f, ; 1000: lib_System.Runtime.Serialization.Formatters.dll.so => 119
	i64 u0xfc719aec26adf9d9, ; 1001: Xamarin.AndroidX.Navigation.Fragment.dll => 262
	i64 u0xfc82690c2fe2735c, ; 1002: Xamarin.AndroidX.Lifecycle.Process.dll => 249
	i64 u0xfc93fc307d279893, ; 1003: System.IO.Pipes.AccessControl.dll => 60
	i64 u0xfcd302092ada6328, ; 1004: System.IO.MemoryMappedFiles.dll => 58
	i64 u0xfd22f00870e40ae0, ; 1005: lib_Xamarin.AndroidX.DrawerLayout.dll.so => 236
	i64 u0xfd49b3c1a76e2748, ; 1006: System.Runtime.InteropServices.RuntimeInformation => 114
	i64 u0xfd536c702f64dc47, ; 1007: System.Text.Encoding.Extensions => 142
	i64 u0xfd583f7657b6a1cb, ; 1008: Xamarin.AndroidX.Fragment => 241
	i64 u0xfd8dd91a2c26bd5d, ; 1009: Xamarin.AndroidX.Lifecycle.Runtime => 250
	i64 u0xfda36abccf05cf5c, ; 1010: System.Net.WebSockets.Client => 87
	i64 u0xfddbe9695626a7f5, ; 1011: Xamarin.AndroidX.Lifecycle.Common => 244
	i64 u0xfeae9952cf03b8cb, ; 1012: tr/Microsoft.Maui.Controls.resources => 332
	i64 u0xfebe1950717515f9, ; 1013: Xamarin.AndroidX.Lifecycle.LiveData.Core.Ktx.dll => 248
	i64 u0xff270a55858bac8d, ; 1014: System.Security.Principal => 136
	i64 u0xff9b54613e0d2cc8, ; 1015: System.Net.Http.Json => 70
	i64 u0xffdb7a971be4ec73 ; 1016: System.ValueTuple.dll => 160
], align 16

@assembly_image_cache_indices = dso_local local_unnamed_addr constant [1017 x i32] [
	i32 47, i32 300, i32 275, i32 18, i32 263, i32 112, i32 179, i32 53,
	i32 217, i32 12, i32 207, i32 93, i32 328, i32 306, i32 334, i32 237,
	i32 77, i32 295, i32 267, i32 17, i32 199, i32 109, i32 335, i32 164,
	i32 24, i32 242, i32 224, i32 169, i32 239, i32 279, i32 175, i32 328,
	i32 15, i32 193, i32 280, i32 103, i32 235, i32 236, i32 18, i32 194,
	i32 15, i32 293, i32 134, i32 102, i32 148, i32 44, i32 329, i32 303,
	i32 282, i32 325, i32 294, i32 180, i32 211, i32 10, i32 200, i32 73,
	i32 272, i32 137, i32 271, i32 238, i32 74, i32 225, i32 72, i32 62,
	i32 234, i32 57, i32 48, i32 132, i32 73, i32 88, i32 252, i32 166,
	i32 99, i32 106, i32 267, i32 149, i32 159, i32 221, i32 312, i32 170,
	i32 177, i32 313, i32 187, i32 88, i32 297, i32 225, i32 9, i32 10,
	i32 56, i32 108, i32 61, i32 127, i32 105, i32 176, i32 125, i32 300,
	i32 26, i32 316, i32 144, i32 104, i32 303, i32 84, i32 322, i32 274,
	i32 126, i32 13, i32 173, i32 331, i32 76, i32 210, i32 253, i32 268,
	i32 4, i32 179, i32 153, i32 45, i32 272, i32 52, i32 35, i32 265,
	i32 320, i32 152, i32 2, i32 194, i32 171, i32 33, i32 91, i32 276,
	i32 84, i32 48, i32 34, i32 47, i32 110, i32 4, i32 124, i32 215,
	i32 50, i32 98, i32 331, i32 61, i32 156, i32 293, i32 154, i32 107,
	i32 54, i32 25, i32 230, i32 121, i32 208, i32 312, i32 289, i32 204,
	i32 298, i32 195, i32 101, i32 63, i32 317, i32 315, i32 285, i32 88,
	i32 289, i32 177, i32 31, i32 77, i32 266, i32 188, i32 240, i32 261,
	i32 333, i32 75, i32 38, i32 311, i32 19, i32 146, i32 43, i32 337,
	i32 226, i32 324, i32 141, i32 99, i32 95, i32 197, i32 157, i32 286,
	i32 330, i32 29, i32 145, i32 62, i32 147, i32 56, i32 309, i32 34,
	i32 165, i32 39, i32 172, i32 241, i32 202, i32 57, i32 338, i32 284,
	i32 97, i32 222, i32 40, i32 312, i32 165, i32 1, i32 14, i32 310,
	i32 83, i32 64, i32 60, i32 199, i32 306, i32 0, i32 196, i32 18,
	i32 283, i32 184, i32 219, i32 116, i32 256, i32 37, i32 111, i32 91,
	i32 99, i32 58, i32 103, i32 296, i32 63, i32 14, i32 109, i32 234,
	i32 74, i32 282, i32 305, i32 189, i32 132, i32 268, i32 123, i32 142,
	i32 270, i32 133, i32 113, i32 298, i32 138, i32 221, i32 291, i32 155,
	i32 164, i32 242, i32 230, i32 204, i32 237, i32 268, i32 104, i32 29,
	i32 273, i32 151, i32 190, i32 260, i32 8, i32 175, i32 218, i32 107,
	i32 169, i32 106, i32 232, i32 30, i32 100, i32 176, i32 180, i32 213,
	i32 8, i32 324, i32 277, i32 239, i32 6, i32 121, i32 298, i32 242,
	i32 249, i32 38, i32 11, i32 3, i32 328, i32 164, i32 326, i32 58,
	i32 92, i32 281, i32 265, i32 3, i32 49, i32 248, i32 111, i32 52,
	i32 145, i32 258, i32 70, i32 294, i32 259, i32 75, i32 87, i32 65,
	i32 96, i32 162, i32 219, i32 140, i32 117, i32 64, i32 318, i32 259,
	i32 5, i32 266, i32 179, i32 141, i32 148, i32 45, i32 305, i32 206,
	i32 196, i32 66, i32 255, i32 86, i32 30, i32 41, i32 106, i32 252,
	i32 77, i32 27, i32 230, i32 201, i32 329, i32 128, i32 75, i32 114,
	i32 335, i32 126, i32 124, i32 244, i32 261, i32 245, i32 16, i32 7,
	i32 131, i32 122, i32 150, i32 46, i32 94, i32 214, i32 205, i32 182,
	i32 190, i32 32, i32 156, i32 319, i32 186, i32 290, i32 213, i32 6,
	i32 215, i32 49, i32 229, i32 157, i32 23, i32 93, i32 307, i32 292,
	i32 46, i32 248, i32 223, i32 253, i32 101, i32 191, i32 33, i32 46,
	i32 85, i32 238, i32 226, i32 152, i32 115, i32 224, i32 16, i32 112,
	i32 144, i32 21, i32 129, i32 72, i32 165, i32 27, i32 206, i32 309,
	i32 302, i32 109, i32 186, i32 301, i32 69, i32 63, i32 198, i32 308,
	i32 117, i32 182, i32 264, i32 299, i32 14, i32 287, i32 127, i32 105,
	i32 112, i32 81, i32 256, i32 196, i32 118, i32 216, i32 54, i32 64,
	i32 25, i32 255, i32 233, i32 78, i32 228, i32 163, i32 44, i32 307,
	i32 40, i32 43, i32 313, i32 206, i32 115, i32 322, i32 26, i32 296,
	i32 254, i32 201, i32 20, i32 195, i32 86, i32 86, i32 233, i32 195,
	i32 262, i32 271, i32 160, i32 26, i32 199, i32 306, i32 55, i32 56,
	i32 332, i32 322, i32 101, i32 209, i32 188, i32 318, i32 21, i32 232,
	i32 130, i32 315, i32 168, i32 50, i32 290, i32 183, i32 123, i32 69,
	i32 174, i32 184, i32 19, i32 269, i32 118, i32 216, i32 66, i32 81,
	i32 128, i32 321, i32 7, i32 331, i32 241, i32 254, i32 297, i32 254,
	i32 11, i32 223, i32 311, i32 237, i32 22, i32 329, i32 308, i32 84,
	i32 227, i32 261, i32 293, i32 138, i32 296, i32 321, i32 90, i32 193,
	i32 17, i32 4, i32 39, i32 126, i32 302, i32 249, i32 181, i32 239,
	i32 92, i32 208, i32 295, i32 23, i32 282, i32 185, i32 247, i32 78,
	i32 102, i32 207, i32 173, i32 243, i32 89, i32 337, i32 217, i32 222,
	i32 162, i32 41, i32 159, i32 333, i32 203, i32 336, i32 152, i32 61,
	i32 120, i32 223, i32 279, i32 278, i32 42, i32 337, i32 184, i32 122,
	i32 215, i32 19, i32 209, i32 154, i32 48, i32 200, i32 213, i32 105,
	i32 301, i32 176, i32 21, i32 270, i32 53, i32 114, i32 104, i32 259,
	i32 32, i32 135, i32 34, i32 313, i32 5, i32 189, i32 271, i32 135,
	i32 49, i32 233, i32 238, i32 157, i32 13, i32 277, i32 260, i32 314,
	i32 327, i32 205, i32 295, i32 326, i32 139, i32 1, i32 325, i32 47,
	i32 302, i32 205, i32 38, i32 338, i32 51, i32 151, i32 255, i32 198,
	i32 246, i32 234, i32 145, i32 68, i32 139, i32 305, i32 53, i32 168,
	i32 220, i32 246, i32 209, i32 244, i32 321, i32 278, i32 51, i32 172,
	i32 181, i32 243, i32 310, i32 240, i32 317, i32 201, i32 292, i32 23,
	i32 13, i32 183, i32 231, i32 270, i32 131, i32 65, i32 149, i32 263,
	i32 320, i32 250, i32 288, i32 284, i32 158, i32 150, i32 300, i32 133,
	i32 299, i32 168, i32 170, i32 235, i32 212, i32 185, i32 264, i32 323,
	i32 31, i32 260, i32 247, i32 89, i32 0, i32 284, i32 134, i32 289,
	i32 108, i32 156, i32 287, i32 285, i32 265, i32 59, i32 170, i32 175,
	i32 138, i32 42, i32 280, i32 320, i32 27, i32 119, i32 97, i32 55,
	i32 66, i32 129, i32 90, i32 134, i32 171, i32 288, i32 174, i32 269,
	i32 272, i32 236, i32 208, i32 251, i32 9, i32 245, i32 316, i32 178,
	i32 7, i32 292, i32 256, i32 123, i32 214, i32 24, i32 192, i32 96,
	i32 71, i32 35, i32 187, i32 309, i32 228, i32 65, i32 118, i32 247,
	i32 37, i32 135, i32 167, i32 327, i32 226, i32 148, i32 1, i32 323,
	i32 161, i32 22, i32 225, i32 211, i32 82, i32 80, i32 20, i32 177,
	i32 92, i32 203, i32 131, i32 246, i32 257, i32 227, i32 330, i32 253,
	i32 39, i32 125, i32 146, i32 129, i32 113, i32 307, i32 280, i32 222,
	i32 314, i32 304, i32 59, i32 52, i32 33, i32 147, i32 153, i32 192,
	i32 155, i32 40, i32 330, i32 182, i32 82, i32 169, i32 6, i32 258,
	i32 197, i32 273, i32 326, i32 319, i32 167, i32 17, i32 163, i32 159,
	i32 83, i32 110, i32 119, i32 207, i32 219, i32 71, i32 72, i32 283,
	i32 50, i32 221, i32 116, i32 12, i32 218, i32 60, i32 214, i32 70,
	i32 304, i32 231, i32 25, i32 116, i32 108, i32 68, i32 5, i32 150,
	i32 212, i32 12, i32 319, i32 178, i32 55, i32 283, i32 122, i32 149,
	i32 183, i32 174, i32 87, i32 120, i32 257, i32 22, i32 79, i32 262,
	i32 96, i32 210, i32 94, i32 127, i32 276, i32 3, i32 202, i32 216,
	i32 202, i32 264, i32 142, i32 161, i32 113, i32 16, i32 97, i32 36,
	i32 332, i32 143, i32 324, i32 327, i32 274, i32 211, i32 45, i32 338,
	i32 273, i32 146, i32 301, i32 303, i32 30, i32 181, i32 336, i32 79,
	i32 243, i32 275, i32 32, i32 73, i32 95, i32 294, i32 102, i32 120,
	i32 36, i32 111, i32 245, i32 42, i32 188, i32 78, i32 291, i32 115,
	i32 130, i32 218, i32 94, i32 191, i32 93, i32 318, i32 100, i32 187,
	i32 136, i32 257, i32 276, i32 193, i32 269, i32 231, i32 275, i32 228,
	i32 288, i32 185, i32 171, i32 137, i32 192, i32 281, i32 285, i32 266,
	i32 186, i32 15, i32 54, i32 334, i32 98, i32 334, i32 158, i32 68,
	i32 143, i32 158, i32 67, i32 191, i32 124, i32 144, i32 297, i32 91,
	i32 336, i32 2, i32 167, i32 278, i32 151, i32 315, i32 240, i32 89,
	i32 81, i32 76, i32 217, i32 143, i32 229, i32 210, i32 132, i32 59,
	i32 117, i32 137, i32 95, i32 28, i32 2, i32 80, i32 136, i32 36,
	i32 190, i32 79, i32 252, i32 317, i32 166, i32 28, i32 9, i32 178,
	i32 325, i32 203, i32 130, i32 232, i32 316, i32 311, i32 121, i32 180,
	i32 37, i32 8, i32 172, i32 279, i32 35, i32 24, i32 251, i32 100,
	i32 41, i32 10, i32 220, i32 299, i32 163, i32 274, i32 290, i32 227,
	i32 281, i32 83, i32 69, i32 263, i32 197, i32 155, i32 224, i32 128,
	i32 141, i32 277, i32 291, i32 200, i32 107, i32 44, i32 212, i32 147,
	i32 310, i32 74, i32 31, i32 82, i32 286, i32 85, i32 250, i32 198,
	i32 29, i32 160, i32 43, i32 258, i32 323, i32 220, i32 140, i32 0,
	i32 110, i32 287, i32 62, i32 173, i32 98, i32 67, i32 139, i32 51,
	i32 140, i32 235, i32 153, i32 85, i32 229, i32 251, i32 286, i32 162,
	i32 308, i32 90, i32 335, i32 333, i32 67, i32 103, i32 267, i32 161,
	i32 314, i32 125, i32 194, i32 11, i32 20, i32 80, i32 189, i32 304,
	i32 154, i32 57, i32 204, i32 76, i32 28, i32 166, i32 133, i32 71,
	i32 119, i32 262, i32 249, i32 60, i32 58, i32 236, i32 114, i32 142,
	i32 241, i32 250, i32 87, i32 244, i32 332, i32 248, i32 136, i32 70,
	i32 160
], align 16

@marshal_methods_number_of_classes = dso_local local_unnamed_addr constant i32 0, align 4

@marshal_methods_class_cache = dso_local local_unnamed_addr global [0 x %struct.MarshalMethodsManagedClass] zeroinitializer, align 8

; Names of classes in which marshal methods reside
@mm_class_names = dso_local local_unnamed_addr constant [0 x ptr] zeroinitializer, align 8

@mm_method_names = dso_local local_unnamed_addr constant [1 x %struct.MarshalMethodName] [
	%struct.MarshalMethodName {
		i64 u0x0000000000000000, ; name: 
		ptr @.MarshalMethodName.0_name; char* name
	} ; 0
], align 8

; get_function_pointer (uint32_t mono_image_index, uint32_t class_index, uint32_t method_token, void*& target_ptr)
@get_function_pointer = internal dso_local unnamed_addr global ptr null, align 8

; Functions

; Function attributes: memory(write, argmem: none, inaccessiblemem: none) "min-legal-vector-width"="0" mustprogress "no-trapping-math"="true" nofree norecurse nosync nounwind "stack-protector-buffer-size"="8" uwtable willreturn
define void @xamarin_app_init(ptr nocapture noundef readnone %env, ptr noundef %fn) local_unnamed_addr #0
{
	%fnIsNull = icmp eq ptr %fn, null
	br i1 %fnIsNull, label %1, label %2

1: ; preds = %0
	%putsResult = call noundef i32 @puts(ptr @.mm.0)
	call void @abort()
	unreachable 

2: ; preds = %1, %0
	store ptr %fn, ptr @get_function_pointer, align 8, !tbaa !3
	ret void
}

; Strings
@.mm.0 = private unnamed_addr constant [40 x i8] c"get_function_pointer MUST be specified\0A\00", align 16

;MarshalMethodName
@.MarshalMethodName.0_name = private unnamed_addr constant [1 x i8] c"\00", align 1

; External functions

; Function attributes: "no-trapping-math"="true" noreturn nounwind "stack-protector-buffer-size"="8"
declare void @abort() local_unnamed_addr #2

; Function attributes: nofree nounwind
declare noundef i32 @puts(ptr noundef) local_unnamed_addr #1
attributes #0 = { memory(write, argmem: none, inaccessiblemem: none) "min-legal-vector-width"="0" mustprogress "no-trapping-math"="true" nofree norecurse nosync nounwind "stack-protector-buffer-size"="8" "target-cpu"="x86-64" "target-features"="+crc32,+cx16,+cx8,+fxsr,+mmx,+popcnt,+sse,+sse2,+sse3,+sse4.1,+sse4.2,+ssse3,+x87" "tune-cpu"="generic" uwtable willreturn }
attributes #1 = { nofree nounwind }
attributes #2 = { "no-trapping-math"="true" noreturn nounwind "stack-protector-buffer-size"="8" "target-cpu"="x86-64" "target-features"="+crc32,+cx16,+cx8,+fxsr,+mmx,+popcnt,+sse,+sse2,+sse3,+sse4.1,+sse4.2,+ssse3,+x87" "tune-cpu"="generic" }

; Metadata
!llvm.module.flags = !{!0, !1}
!0 = !{i32 1, !"wchar_size", i32 4}
!1 = !{i32 7, !"PIC Level", i32 2}
!llvm.ident = !{!2}
!2 = !{!".NET for Android remotes/origin/release/10.0.1xx @ d611c1210ffde26280ee0faddd04e7621af41f1e"}
!3 = !{!4, !4, i64 0}
!4 = !{!"any pointer", !5, i64 0}
!5 = !{!"omnipotent char", !6, i64 0}
!6 = !{!"Simple C++ TBAA"}
