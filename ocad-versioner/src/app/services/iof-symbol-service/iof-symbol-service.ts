import { isNil } from 'lodash-es';

export class IofSymbolHelper {
  static getSymbolName(symbolNameInGeoJson?: string | null): string {
    if (isNil(symbolNameInGeoJson)) return '';
    if (symbolNameInGeoJson.length !== 6) return symbolNameInGeoJson;
    if (isNaN(+symbolNameInGeoJson)) return symbolNameInGeoJson;
    const mainSymbolNumber = symbolNameInGeoJson.slice(0, 3);

    return `#_isom2017SymbolName${mainSymbolNumber}`;
  }
  static getSymbolNumber(symbolNameInGeoJson?: string | null): string {
    if (isNil(symbolNameInGeoJson)) return '';
    if (symbolNameInGeoJson.length !== 6) return '';
    if (isNaN(+symbolNameInGeoJson)) return '';
    const mainSymbolNumber = symbolNameInGeoJson.slice(0, 3);
    const specificSymbolNumber = symbolNameInGeoJson.slice(3);
    if (+specificSymbolNumber > 0)
      return `${mainSymbolNumber}.${parseInt(specificSymbolNumber, 10)}`;
    return `${mainSymbolNumber}`;
  }
}
