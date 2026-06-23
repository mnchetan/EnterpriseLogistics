import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface CargoBox {
  boxId: number;
  stopSequence: number;
  length: number;
  width: number;
  height: number;
  weight: number;
  isFragile: boolean;
  
  // Add this line to fix the TS2551 error!
  isPacked: boolean; 
  
  packedX: number;
  packedY: number;
  packedZ: number;
}

export interface PackResponse {
  message: string;
  packedBoxes: CargoBox[];
}

@Injectable({
  providedIn: 'root'
})
export class LogisticsService {
  // Update this to match your .NET launchSettings.json port
  private apiUrl = 'http://localhost:5059/Algorithm';

  constructor(private http: HttpClient) { }

  public calculatePacking(routeId: number): Observable<PackResponse> {
    return this.http.post<PackResponse>(`${this.apiUrl}/pack/${routeId}`, {});
  }
}