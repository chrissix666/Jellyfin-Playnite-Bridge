// Jellyfin 12 build: the types that moved between Jellyfin 10.10 (net8.0 build) and Jellyfin 12.x (net10.0 build), in one
// place, so the code compiles unchanged for both targets.
//   User, TrickplayInfo                        Jellyfin.Data.Entities (10.10) -> Jellyfin.Database.Implementations.Entities (12.x)
//   SortOrder, PreferenceKind, PermissionKind  Jellyfin.Data.Enums (10.10)    -> Jellyfin.Database.Implementations.Enums (12.x)
//                                              (ItemSortBy, UnratedItem and BaseItemKind stay in Jellyfin.Data.Enums on both)
//   GetPreference, GetPreferenceValues         methods of User (10.10)        -> extension methods of Jellyfin.Data.UserEntityExtensions (12.x)
#if NET10_0_OR_GREATER
global using Jellyfin.Data;
global using PermissionKind = Jellyfin.Database.Implementations.Enums.PermissionKind;
global using PreferenceKind = Jellyfin.Database.Implementations.Enums.PreferenceKind;
global using SortOrder = Jellyfin.Database.Implementations.Enums.SortOrder;
global using TrickplayInfo = Jellyfin.Database.Implementations.Entities.TrickplayInfo;
global using User = Jellyfin.Database.Implementations.Entities.User;
#else
global using PermissionKind = Jellyfin.Data.Enums.PermissionKind;
global using PreferenceKind = Jellyfin.Data.Enums.PreferenceKind;
global using SortOrder = Jellyfin.Data.Enums.SortOrder;
global using TrickplayInfo = Jellyfin.Data.Entities.TrickplayInfo;
global using User = Jellyfin.Data.Entities.User;
#endif
