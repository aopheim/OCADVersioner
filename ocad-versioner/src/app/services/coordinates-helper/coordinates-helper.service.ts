import { isNil } from 'lodash-es'
import { toLatLon } from 'utm'
import fullEpsgIndex from 'epsg-index/all.json'
import { EpsgIndex } from '../../components/app-settings-modal/app-settings-modal.component'
import proj4 from 'proj4'

export class CoordinatesHelper {
    // WGS84
    private static Wgs84EpsgCode: number = 4326
    // Google Maps Global Mercator. Default EPSG of Leaflet
    public static DefaultEpsgCode: number = 900913

    public static getLatLongCoordinateFromUtm(
        easting: number,
        northing: number,
        zoneNumber: number,
        zoneLetter: string
    ): {
        latitude: number
        longitude: number
    } {
        return toLatLon(
            easting,
            northing,
            zoneNumber,
            zoneLetter,
            undefined,
            false
        )
    }

    public static getLatLongCoordinatesFromEpsgCode(
        easting: number,
        northing: number,
        epsgCode: number | null
    ): {
        latitude: number
        longitude: number
    } {
        if (isNil(epsgCode)) epsgCode = this.DefaultEpsgCode
        const index = fullEpsgIndex as EpsgIndex
        let fromProj4 = index[epsgCode + '']?.proj4
        if (isNil(fromProj4)) fromProj4 = index[this.DefaultEpsgCode]!.proj4
        const toProj4 = index[CoordinatesHelper.Wgs84EpsgCode + ''].proj4
        const transform = proj4(fromProj4!, toProj4!)
        const latLong = transform.forward({ x: easting, y: northing })

        return { longitude: latLong.x, latitude: latLong.y }
    }
}
