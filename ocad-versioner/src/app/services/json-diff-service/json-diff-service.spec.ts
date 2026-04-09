import { JsonDiffService } from './json-diff-service'
import {
    V1Empty,
    V2OneKnoll,
    V3OneKnollMoved,
    V4DeletedKnoll_AddedDepression,
    V5AddedContour,
    V6RemovedAllObjects,
} from './json-diff-service.spec.models'

describe('JsonDiffService', () => {
    it('Empty input should give empty results', () => {
        const res = JsonDiffService.calculateJsonDiff(
            { features: [], type: 'FeatureCollection' },
            { features: [], type: 'FeatureCollection' }
        )

        expect(res.added.length).toBe(0)
        expect(res.edited.length).toBe(0)
        expect(res.deleted.length).toBe(0)
    })

    it('Adding one knoll should give one element as added', () => {
        const res = JsonDiffService.calculateJsonDiff(V1Empty, V2OneKnoll)

        expect(res.deleted.length).toBe(0)
        expect(res.edited.length).toBe(0)
        expect(res.added.length).toBe(1)
    })

    it('Adding one knoll should give added element with correct properties', () => {
        const res = JsonDiffService.calculateJsonDiff(V1Empty, V2OneKnoll)

        const added = res.added[0]
        expect(added.createdAtUtc).toBeTruthy()
        expect(added.lastEditedAtUtc).toBeFalsy()
        expect(added.symbolName).toBe('#_isom2017SymbolName109')
        expect(added.symbolNumber).toBe('109')
    })

    it('Moving knoll should give one element in edited', () => {
        const res = JsonDiffService.calculateJsonDiff(
            V2OneKnoll,
            V3OneKnollMoved
        )

        expect(res.deleted.length).toBe(0)
        expect(res.added.length).toBe(0)
        expect(res.edited.length).toBe(1)
    })

    it('Moving knoll should give knoll element in edited with correct properties', () => {
        const res = JsonDiffService.calculateJsonDiff(
            V2OneKnoll,
            V3OneKnollMoved
        )

        const knoll = res.edited[0]
        expect(knoll.areaSymbolDiff).toBeNull()
        expect(knoll.lineSymbolDiff).toBeNull()
        expect(knoll.lastEditedAtUtc).toBeTruthy()
        expect(knoll.createdAtUtc).toBeTruthy()
        expect(knoll.lastEditedAtUtc).not.toBe(knoll.createdAtUtc)
        expect(knoll.symbolName).toBe('#_isom2017SymbolName109')
        expect(knoll.pointSymbolDiff?.movementInMeters).toBeGreaterThan(0)
    })

    it('Deleting knoll, added depression should give one in deleted, one in added', () => {
        const res = JsonDiffService.calculateJsonDiff(
            V3OneKnollMoved,
            V4DeletedKnoll_AddedDepression
        )

        expect(res.added.length).toBe(1)
        expect(res.edited.length).toBe(0)
        expect(res.deleted.length).toBe(1)
    })

    it('Deleting knoll, added depression should give depression in added', () => {
        const res = JsonDiffService.calculateJsonDiff(
            V3OneKnollMoved,
            V4DeletedKnoll_AddedDepression
        )

        const depression = res.added[0]
        expect(depression.symbolName).toBe('#_isom2017SymbolName112')
        expect(depression.symbolNumber).toBe('112')
        expect(depression.createdAtUtc).toBeTruthy()
        expect(depression.lastEditedAtUtc).toBeFalsy()
    })

    it('Deleting knoll, added depression should give knoll in deleted', () => {
        const res = JsonDiffService.calculateJsonDiff(
            V3OneKnollMoved,
            V4DeletedKnoll_AddedDepression
        )

        const knoll = res.deleted[0]
        expect(knoll.symbolName).toBe('#_isom2017SymbolName109')
        expect(knoll.symbolNumber).toBe('109')
        expect(knoll.createdAtUtc).toBeTruthy()
        expect(knoll.lastEditedAtUtc).toBeTruthy()
        expect(knoll.lastEditedAtUtc).not.toBe(knoll.createdAtUtc)
    })

    it('Adding contour should give one element in added', () => {
        const res = JsonDiffService.calculateJsonDiff(
            V4DeletedKnoll_AddedDepression,
            V5AddedContour
        )

        expect(res.added.length).toBe(1)
        expect(res.deleted.length).toBe(0)
        expect(res.edited.length).toBe(0)
    })

    it('Added contour should give one element in added with correct properties', () => {
        const res = JsonDiffService.calculateJsonDiff(
            V4DeletedKnoll_AddedDepression,
            V5AddedContour
        )
        const contour = res.added[0]

        expect(contour.createdAtUtc).toBeTruthy()
        expect(contour.lastEditedAtUtc).toBeFalsy()
        expect(contour.symbolName).toBe('#_isom2017SymbolName101')
        expect(contour.symbolNumber).toBe('101')
    })

    it('Removing all features should give two deleted elements', () => {
        const res = JsonDiffService.calculateJsonDiff(
            V5AddedContour,
            V6RemovedAllObjects
        )

        expect(res.added.length).toBe(0)
        expect(res.edited.length).toBe(0)
        expect(res.deleted.length).toBe(2)
    })

    it('Removing all features should give deleted contour and depression', () => {
        const res = JsonDiffService.calculateJsonDiff(
            V5AddedContour,
            V6RemovedAllObjects
        )

        const contour = res.deleted.filter((d) => d.symbolNumber === '101')
        const depression = res.deleted.filter((d) => d.symbolNumber === '112')
        expect(contour).toBeTruthy()
        expect(depression).toBeTruthy()
    })
})
