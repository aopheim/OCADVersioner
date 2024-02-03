export interface AppSettings {
  georeferencing?: GeoReferenceSettings;
}

export interface GeoReferenceSettings {
  epsgNumber?: number | null;
}
