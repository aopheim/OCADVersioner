/// <reference lib="webworker" />

import { OcadDiffDto } from '../components/ocad-diff-table/ocad-diff-table/ocad-diff-table.models'
import { JsonDiffService } from '../services/json-diff-service/json-diff-service'
import { readOcad, ocadToGeoJson } from 'ocad2geojson-test'
import { Buffer } from 'buffer'
import bbox from '@turf/bbox'
import { CoordinatesHelper } from '../services/coordinates-helper/coordinates-helper.service'

addEventListener('message', async ({ data }) => {
    function reportProgress(progress: number): void {
        postMessage({ progress })
    }

    try {
        const { newVersionBuffer, oldVersionBuffer, epsgcode } = data as {
            newVersionBuffer: ArrayBuffer
            oldVersionBuffer: ArrayBuffer | null
            epsgcode?: number
        }

        // Parse new version OCAD to GeoJSON inside worker
        const newestOcad = await readOcad(Buffer.from(newVersionBuffer))
        const newestFeatureCollection = await ocadToGeoJson(newestOcad, {
            generateSymbolElements: false,
        })

        // Compute Bounding Box inside worker
        const bboxOfMap = bbox(newestFeatureCollection)
        const currentEpsg = epsgcode ?? CoordinatesHelper.DefaultEpsgCode
        const minLatLon = CoordinatesHelper.getLatLongCoordinatesFromEpsgCode(
            bboxOfMap[0],
            bboxOfMap[1],
            currentEpsg
        )
        const maxLatLon = CoordinatesHelper.getLatLongCoordinatesFromEpsgCode(
            bboxOfMap[2],
            bboxOfMap[3],
            currentEpsg
        )

        let calculatedBbox: any = null
        if (minLatLon && maxLatLon) {
            calculatedBbox = [
                [minLatLon.latitude, minLatLon.longitude],
                [maxLatLon.latitude, maxLatLon.longitude],
            ]
        }

        // Parse old version OCAD to GeoJSON inside worker (if it exists)
        let oldestFeatureCollection: any = {
            type: 'FeatureCollection',
            features: [],
        }
        if (oldVersionBuffer) {
            const oldestOcad = await readOcad(Buffer.from(oldVersionBuffer))
            oldestFeatureCollection = await ocadToGeoJson(oldestOcad, {
                generateSymbolElements: false,
            })
        }

        // Calculate Diff
        const diff: OcadDiffDto = JsonDiffService.calculateJsonDiff(
            oldestFeatureCollection,
            newestFeatureCollection,
            epsgcode,
            reportProgress
        )

        // Post both diff and bounding box back to main thread
        postMessage({ diff, bbox: calculatedBbox })
    } catch (error: any) {
        console.error('Error in diff-service.worker:', error)
        postMessage({ error: error?.message || 'Error processing map diff' })
    }
})
