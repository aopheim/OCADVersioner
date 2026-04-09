import { JsonDiffService } from './json-diff-service'
import {
    V1_GeoreferencedEmptyMap,
    V2_Add100mRoad,
    V3_Add400mPath,
    V4_AddPolygon,
    V5_AddPoint,
    V6_MovePoint100m,
} from './json-diff-service.spec.models.distance'

///
/// Tests here are skipped due to geometry of Bezier curves is not calculated correctly. https://github.com/aopheim/OCADVersioner/issues/23
///

describe('JsonDiffService - Distance tests', () => {
    const EpsgCodeInTestFile: number = 25832
    it('Add 100m road should give 100m road added', () => {
        const res = JsonDiffService.calculateJsonDiff(
            V1_GeoreferencedEmptyMap,
            V2_Add100mRoad,
            EpsgCodeInTestFile
        )

        expect(res.added.length).toBe(1)
        expect(res.added[0].symbolLengthInMeters).toBeGreaterThan(98)
        expect(res.added[0].symbolLengthInMeters).toBeLessThan(102)
    })

    it('Deleting 100m road should give 100m road deleted', () => {
        const res = JsonDiffService.calculateJsonDiff(
            V2_Add100mRoad,
            V1_GeoreferencedEmptyMap,
            EpsgCodeInTestFile
        )

        expect(res.deleted.length).toBe(1)
        expect(res.deleted[0].symbolLengthInMeters).toBeGreaterThan(98)
        expect(res.deleted[0].symbolLengthInMeters).toBeLessThan(102)
    })

    xit('Adding 400m circular path should give 400m added', () => {
        const res = JsonDiffService.calculateJsonDiff(
            V1_GeoreferencedEmptyMap,
            V3_Add400mPath,
            EpsgCodeInTestFile
        )

        expect(res.added.length).toBe(1)
        expect(res.added[0].symbolLengthInMeters).toBeGreaterThan(398)
        expect(res.added[0].symbolLengthInMeters).toBeLessThan(402)
    })

    it('Adding polygon should give added area', () => {
        const res = JsonDiffService.calculateJsonDiff(
            V1_GeoreferencedEmptyMap,
            V4_AddPolygon,
            EpsgCodeInTestFile
        )

        expect(res.added.length).toBe(1)
        expect(res.added[0].symbolAreaInSquareMeters).toBeGreaterThan(10190)
        expect(res.added[0].symbolLengthInMeters).toBeLessThan(10193)
    })

    xit('Deleting polygon should give deleted area', () => {
        const res = JsonDiffService.calculateJsonDiff(
            V4_AddPolygon,
            V1_GeoreferencedEmptyMap,
            EpsgCodeInTestFile
        )

        expect(res.deleted.length).toBe(1)
        expect(res.deleted[0].symbolAreaInSquareMeters).toBeGreaterThan(10190)
        expect(res.deleted[0].symbolAreaInSquareMeters).toBeLessThan(10193)
    })

    it('Moving point 100m should give 100m moved in edited', () => {
        const res = JsonDiffService.calculateJsonDiff(
            V5_AddPoint,
            V6_MovePoint100m,
            EpsgCodeInTestFile
        )

        expect(res.added.length).toBe(0)
        expect(res.deleted.length).toBe(0)
        expect(res.edited.length).toBe(1)
        expect(res.edited[0].pointSymbolDiff?.movementInMeters).toBeGreaterThan(
            98
        )
        expect(res.edited[0].pointSymbolDiff?.movementInMeters).toBeLessThan(
            102
        )
    })
})
