import { NgModule } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
// 1. IMPORT THE HTTP CLIENT MODULE
import { HttpClientModule } from '@angular/common/http'; 

import { AppComponent } from './app.component';

@NgModule({
  declarations: [
    AppComponent
  ],
  imports: [
    BrowserModule,
    // 2. ADD IT TO THE IMPORTS ARRAY
    HttpClientModule 
  ],
  providers: [],
  bootstrap: [AppComponent]
})
export class AppModule { }