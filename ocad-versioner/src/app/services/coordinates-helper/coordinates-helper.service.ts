import { toLatLon } from 'utm';

export class CoordinatesHelper {
  public static getLatLongCoordinateFromUtm(
    easting: number,
    northing: number,
    zoneNumber: number,
    zoneLetter: string
  ) {
    return toLatLon(easting, northing, zoneNumber, zoneLetter);
  }
}
