import { IofSymbolHelper } from './iof-symbol-service'

describe('IofSymbolService', () => {
    const symbolNameTestCases = [
        { geoJsonName: 109000, expectedName: '#_isom2017SymbolName109' },
        { geoJsonName: 10900, expectedName: '10900' },
        { geoJsonName: 1090000, expectedName: '1090000' },
        { geoJsonName: null, expectedName: '' },
        { geoJsonName: undefined, expectedName: '' },
        { geoJsonName: 109.0, expectedName: '109' },
    ]

    symbolNameTestCases.forEach((test) => {
        it(`SymbolName ${test.geoJsonName} in geoJson is expected to be ${test.expectedName}`, () => {
            expect(IofSymbolHelper.getSymbolName(test.geoJsonName)).toEqual(
                test.expectedName
            )
        })
    })

    const symbolNumberTestCases = [
        { geoJsonName: 109000, expectedNumber: '109' },
        { geoJsonName: 109100, expectedNumber: '109.100' },
        { geoJsonName: 10900, expectedNumber: '' },
        { geoJsonName: 1090000, expectedNumber: '' },
        { geoJsonName: null, expectedNumber: '' },
    ]

    symbolNumberTestCases.forEach((test) => {
        it(`SymbolName ${test.geoJsonName} in geoJson is expected to be symbolNumber ${test.expectedNumber}`, () => {
            expect(IofSymbolHelper.getSymbolNumber(test.geoJsonName)).toEqual(
                test.expectedNumber
            )
        })
    })
})
