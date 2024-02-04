export interface AppSettings {
  georeferencing?: GeoReferenceSettings;
  languageSelection?: LanguageSelectionSettings;
}

export interface GeoReferenceSettings {
  epsgNumber?: number | null;
}

export interface LanguageSelectionSettings {
  selectedLanguageCode?: string | null;
}
