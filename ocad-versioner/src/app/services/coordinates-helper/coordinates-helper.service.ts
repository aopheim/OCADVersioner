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
    // if (!this.isValidEasting(easting) || !this.isValidNorthing(northing))
    //   return { latitude: easting, longitude: northing };
    return toLatLon(
      easting,
      northing,
      zoneNumber,
      zoneLetter,
      undefined,
      false
    );
  }
}
