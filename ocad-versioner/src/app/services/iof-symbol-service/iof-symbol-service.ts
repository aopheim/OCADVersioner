import { isNil } from 'lodash-es';

export class IofSymbolHelper {
  static getSymbolName(symbolNameInGeoJson?: number | null): string {
    if (isNil(symbolNameInGeoJson)) return '';
    if (symbolNameInGeoJson >= 1e6 || symbolNameInGeoJson <= 99999)
      return '' + symbolNameInGeoJson;
    const mainSymbolNumber = ('' + symbolNameInGeoJson).slice(0, 3);

    return `#_isom2017SymbolName${mainSymbolNumber}`;
  }
  static getSymbolNumber(symbolNameInGeoJson?: number | null): string {
    if (isNil(symbolNameInGeoJson)) return '';
    if (symbolNameInGeoJson >= 1e6 || symbolNameInGeoJson <= 99999) return '';
    const mainSymbolNumber = ('' + symbolNameInGeoJson).slice(0, 3);
    const specificSymbolNumber = ('' + symbolNameInGeoJson).slice(3);
    if (+specificSymbolNumber > 0)
      return `${mainSymbolNumber}.${parseInt(specificSymbolNumber, 10)}`;
    return `${mainSymbolNumber}`;
  }
}
