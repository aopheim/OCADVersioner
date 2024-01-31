import { isNil } from 'lodash-es';
import proj4 from 'proj4';
import { toLatLon } from 'utm';

export class CoordinatesHelper {
  public static getLatLongCoordinateFromUtm(
    easting: number,
    northing: number,
    zoneNumber: number,
    zoneLetter: string
  ): {
    latitude: number;
    longitude: number;
  } {
    return toLatLon(
      easting,
      northing,
      zoneNumber,
      zoneLetter,
      undefined,
      false
    );
  }

  public static getLatLongCoordinatesFromEpsgCode(
    easting: number,
    northing: number,
    epsgCode: number | null
  ): {
    latitude: number;
    longitude: number;
  } {
    if (isNil(epsgCode)) return { latitude: easting, longitude: northing };
    const latLong = proj4(`EPSG:${epsgCode}`, 'EPSG:4326', [easting, northing]);
    return { latitude: latLong[0], longitude: latLong[1] };
  }
}
