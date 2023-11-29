import { FeatureCollection, GeoJsonObject } from 'geojson';
import { JsonDiffService } from './json-diff-service';
import {
  Empty,
  OneKnoll,
  PointLineArea,
  pointLineAreaMoved as PointLineAreaMoved,
} from './json-diff-service.spec.models';

describe('JsonDiffService', () => {
  let jsonDiffService: JsonDiffService;
  beforeEach(() => {
    jsonDiffService = new JsonDiffService();
  });

  it('Empty input should give empty results', () => {
    const res = jsonDiffService.getJsonDiff(
      { features: [], type: 'FeatureCollection' },
      { features: [], type: 'FeatureCollection' }
    );

    expect(res.added.length).toBe(0);
    expect(res.edited.length).toBe(0);
    expect(res.deleted.length).toBe(0);
  });

  it('Adding one knoll should give one element as added', () => {
    const res = jsonDiffService.getJsonDiff(Empty, OneKnoll);

    expect(res.deleted.length).toBe(0);
    expect(res.edited.length).toBe(0);
    expect(res.added.length).toBe(1);
    const added = res.added[0];
    expect(added.createdAtUtc).toBeTruthy();
    expect(added.lastEditBy).toBeNull();
    expect(added.lastEditedAtUtc).toBe(added.createdAtUtc);
    expect(added.symbolName).toBe('Kolle');
    expect(added.symbolNumber).toBe('109');
  });

  it('Adding one knoll should added element with correct properties', () => {
    const res = jsonDiffService.getJsonDiff(Empty, OneKnoll);

    const added = res.added[0];
    expect(added.createdAtUtc).toBeTruthy();
    expect(added.lastEditBy).toBeNull();
    expect(added.lastEditedAtUtc).toBe(added.createdAtUtc);
    expect(added.symbolName).toBe('Høydepunkt');
    expect(added.symbolNumber).toBe('109');
  });

  it('Moving point, line, polygon should give three elements in edited', () => {
    const res = jsonDiffService.getJsonDiff(PointLineArea, PointLineAreaMoved);

    expect(res.deleted.length).toBe(0);
    expect(res.added.length).toBe(0);
    expect(res.edited.length).toBe(3);
  });

  it('Moving knoll should give knoll element in edited with correct properties', () => {
    const res = jsonDiffService.getJsonDiff(PointLineArea, PointLineAreaMoved);

    const knoll = res.edited.filter((e) => e.symbolNumber === '109')[0];
    expect(knoll.areaSymbolDiff).toBeNull();
    expect(knoll.lineSymbolDiff).toBeNull();
    expect(knoll.lastEditBy).toBeNull();
    expect(knoll.lastEditedAtUtc).toBeTruthy();
    expect(knoll.createdAtUtc).toBeTruthy();
    expect(knoll.lastEditedAtUtc).not.toBe(knoll.createdAtUtc);
    expect(knoll.symbolName).toBe('Høydepunkt');
    expect(knoll.pointSymbolDiff?.movementInMeters).toBeGreaterThan(0);
  });

  it('Moving contour should give contour element in edited with correct properties', () => {
    const res = jsonDiffService.getJsonDiff(PointLineArea, PointLineAreaMoved);

    const contour = res.edited.filter((e) => e.symbolNumber === '101')[0];
    expect(contour.areaSymbolDiff).toBeNull();
    expect(contour.pointSymbolDiff).toBeNull();
    expect(contour.lineSymbolDiff?.lengthDiffInPercent).toBeGreaterThan(0);
    expect(contour.lastEditBy).toBeNull();
    expect(contour.lastEditedAtUtc).toBeTruthy();
    expect(contour.createdAtUtc).toBeTruthy();
    expect(contour.lastEditedAtUtc).not.toBe(contour.createdAtUtc);
    expect(contour.symbolName).toBe('Høydekurve');
  });

  it('Moving area should give area element in edited with correct properties', () => {
    const res = jsonDiffService.getJsonDiff(PointLineArea, PointLineAreaMoved);

    const area = res.edited.filter((e) => e.symbolNumber === '403')[0];
    expect(area.lineSymbolDiff).toBeNull();
    expect(area.pointSymbolDiff).toBeNull();
    expect(area.areaSymbolDiff?.areaDiffInPercent).toBeGreaterThan(0);
    expect(area.lastEditBy).toBeNull();
    expect(area.lastEditedAtUtc).toBeTruthy();
    expect(area.createdAtUtc).toBeTruthy();
    expect(area.lastEditedAtUtc).not.toBe(area.createdAtUtc);
    expect(area.symbolName).toBe('Åpent område');
  });

  it('Deleting point, line, polygon should give three elements in deleted', () => {
    const res = jsonDiffService.getJsonDiff(PointLineArea, Empty);

    expect(res.deleted.length).toBe(3);
    expect(res.added.length).toBe(0);
    expect(res.edited.length).toBe(0);
  });

  it('Deleting knoll should give knoll element in deleted with correct properties', () => {
    const res = jsonDiffService.getJsonDiff(PointLineArea, Empty);

    const knoll = res.deleted.filter((e) => e.symbolNumber === '109')[0];
    expect(knoll.lastEditBy).toBeNull();
    expect(knoll.lastEditedAtUtc).toBeTruthy();
    expect(knoll.createdAtUtc).toBeTruthy();
    expect(knoll.lastEditedAtUtc).not.toBe(knoll.createdAtUtc);
    expect(knoll.symbolName).toBe('Høydepunkt');
  });

  it('Deleting contour should give contour element in deleted with correct properties', () => {
    const res = jsonDiffService.getJsonDiff(PointLineArea, Empty);

    const contour = res.deleted.filter((e) => e.symbolNumber === '101')[0];
    expect(contour.lastEditBy).toBeNull();
    expect(contour.lastEditedAtUtc).toBeTruthy();
    expect(contour.createdAtUtc).toBeTruthy();
    expect(contour.lastEditedAtUtc).not.toBe(contour.createdAtUtc);
    expect(contour.symbolName).toBe('Høydekurve');
  });

  it('Deleting area should give area element in deleted with correct properties', () => {
    const res = jsonDiffService.getJsonDiff(PointLineArea, Empty);

    const area = res.deleted.filter((e) => e.symbolNumber === '403')[0];
    expect(area.lastEditBy).toBeNull();
    expect(area.lastEditedAtUtc).toBeTruthy();
    expect(area.createdAtUtc).toBeTruthy();
    expect(area.lastEditedAtUtc).not.toBe(area.createdAtUtc);
    expect(area.symbolName).toBe('Åpent område');
  });
});
