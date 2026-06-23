import { Component, ElementRef, OnInit, ViewChild, AfterViewInit, ChangeDetectorRef } from '@angular/core';
import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls';
import { DragControls } from 'three/examples/jsm/controls/DragControls';
import { LogisticsService, CargoBox } from './logistics.service';

@Component({
  selector: 'app-root',
  templateUrl: 'app.component.html',
  styleUrls: ['app.component.css']
})
export class AppComponent implements OnInit, AfterViewInit {
  @ViewChild('rendererContainer', { static: true }) rendererContainer!: ElementRef;
  
  public packedBoxes: CargoBox[] = [];

  private scene!: THREE.Scene;
  private camera!: THREE.PerspectiveCamera;
  private renderer!: THREE.WebGLRenderer;
  private truckGroup = new THREE.Group();
  
  // NEW: Control variables
  private orbitControls!: OrbitControls;
  private dragControls!: DragControls;
  private draggableMeshes: THREE.Mesh[] = [];

  private TRUCK_L = 5300;
  private TRUCK_W = 2400;
  private TRUCK_H = 2600;

  constructor(
    private logisticsService: LogisticsService,
    private cdr: ChangeDetectorRef // <-- ADD THIS
  ) {}

  ngOnInit(): void {}

  ngAfterViewInit(): void {
    this.init3DScene();
    
    window.addEventListener('resize', () => {
      const container = this.rendererContainer.nativeElement;
      this.camera.aspect = container.clientWidth / container.clientHeight;
      this.camera.updateProjectionMatrix();
      this.renderer.setSize(container.clientWidth, container.clientHeight);
    });
  }

  private init3DScene() {
    const container = this.rendererContainer.nativeElement;

    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color('#1e1e1e');
    
    this.camera = new THREE.PerspectiveCamera(45, container.clientWidth / container.clientHeight, 1, 20000);
    this.camera.position.set(this.TRUCK_W * 1.8, this.TRUCK_H * 1.5, this.TRUCK_L * 1.5);
    this.camera.lookAt(this.TRUCK_W / 2, this.TRUCK_H / 2, this.TRUCK_L / 2);

    this.renderer = new THREE.WebGLRenderer({ antialias: true });
    this.renderer.setSize(container.clientWidth, container.clientHeight);
    container.appendChild(this.renderer.domElement);

    // NEW: Initialize Orbit Controls for 360 viewing
    this.orbitControls = new OrbitControls(this.camera, this.renderer.domElement);
    // Set the target to the center of the truck so it orbits perfectly around it
    this.orbitControls.target.set(this.TRUCK_W / 2, this.TRUCK_H / 2, this.TRUCK_L / 2);
    this.orbitControls.update();

    const ambientLight = new THREE.AmbientLight(0xffffff, 0.7);
    this.scene.add(ambientLight);
    const dirLight = new THREE.DirectionalLight(0xffffff, 0.6);
    dirLight.position.set(5000, 5000, 5000);
    this.scene.add(dirLight);

    this.drawTruckWireframe();
    this.scene.add(this.truckGroup);

    const animate = () => {
      requestAnimationFrame(animate);
      // OrbitControls requires an update call in the animation loop if damping/auto-rotate is enabled
      this.orbitControls.update(); 
      this.renderer.render(this.scene, this.camera);
    };
    animate();
  }

  public runAlgorithm(routeId: number) {
    this.logisticsService.calculatePacking(routeId).subscribe({
      next: (res) => {
        this.packedBoxes = res.packedBoxes;
        this.renderBoxes(res.packedBoxes);
      },
      error: (err) => console.error('Failed to run packing algorithm', err)
    });
  }

  private renderBoxes(boxes: CargoBox[]) {
    // 1. Cleanup old boxes and controls
    const toRemove = this.truckGroup.children.filter(c => c.type === 'Mesh' || c.type === 'Group');
    toRemove.forEach(mesh => this.truckGroup.remove(mesh));
    this.draggableMeshes = [];
    if (this.dragControls) {
      this.dragControls.dispose();
    }

    // 2. Build new boxes
    boxes.forEach(box => {
      if (!box.isPacked) return;

      const color = box.isFragile ? 0xff4444 : 0x4444ff;
      const geometry = new THREE.BoxGeometry(box.width, box.height, box.length);
      const material = new THREE.MeshLambertMaterial({ color: color, transparent: true, opacity: 0.85 });
      const mesh = new THREE.Mesh(geometry, material);

      const centerX = box.packedX + (box.width / 2);
      const centerY = box.packedZ + (box.height / 2); 
      const centerZ = box.packedY + (box.length / 2); 

      mesh.position.set(centerX, centerY, centerZ);
      
      // Store reference to the original box data in the mesh's user data
      mesh.userData = { originalBox: box };

      const edges = new THREE.EdgesGeometry(geometry);
      const edgeLine = new THREE.LineSegments(edges, new THREE.LineBasicMaterial({ color: 0xffffff, transparent: true, opacity: 0.3 }));
      mesh.add(edgeLine);

      this.truckGroup.add(mesh);
      this.draggableMeshes.push(mesh); // Add to our drag array
    });

    // 3. Initialize Drag Controls
    this.dragControls = new DragControls(this.draggableMeshes, this.camera, this.renderer.domElement);

    // Disable camera orbit when user clicks a box to drag it
    this.dragControls.addEventListener('dragstart', (event) => {
      this.orbitControls.enabled = false;
      
      // 1. Cast the generic Object3D to a Mesh first
      const mesh = event.object as THREE.Mesh;
      
      // 2. Safely modify the material opacity
      (mesh.material as THREE.Material).opacity = 0.5;
    });

    // Re-enable camera orbit when user lets go
    this.dragControls.addEventListener('dragend', (event) => {
      this.orbitControls.enabled = true;
      
      const mesh = event.object as THREE.Mesh;
      (mesh.material as THREE.Material).opacity = 0.85;

      const boxData = mesh.userData['originalBox'] as CargoBox;
      
      boxData.packedX = Math.round(mesh.position.x - (boxData.width / 2));
      boxData.packedZ = Math.round(mesh.position.y - (boxData.height / 2));
      boxData.packedY = Math.round(mesh.position.z - (boxData.length / 2));
      
      // 1. Manually trigger Angular to update the HTML table
      this.cdr.detectChanges(); 

      // 2. FIRE THE API CALL TO SAVE THE OVERRIDE TO SQL SERVER!
      this.logisticsService.updateBoxCoordinates(boxData).subscribe({
        next: () => console.log(`[DB SUCCESS] Box ${boxData.boxId} saved to DB.`),
        error: (err) => console.error(`[DB ERROR] Failed to save Box ${boxData.boxId}`, err)
      });
    });
  }

  public fetchCurrentPlan(routeId: number) {
    this.logisticsService.loadExistingPlan(routeId).subscribe({
      next: (data) => {
        // Map raw DB columns (camelCased by default JSON serialization) to our interface
        this.packedBoxes = data.map(row => ({
          boxId: row.boxId || row.BoxId,
          stopSequence: row.sequenceNumber || row.SequenceNumber,
          length: row.lengthMm || row.LengthMm,
          width: row.widthMm || row.WidthMm,
          height: row.heightMm || row.HeightMm,
          weight: row.weightKg || row.WeightKg,
          isFragile: row.isFragile || row.IsFragile,
          isPacked: row.isPacked || row.IsPacked,
          packedX: row.packedX || row.PackedX,
          packedY: row.packedY || row.PackedY,
          packedZ: row.packedZ || row.PackedZ
        }));
        
        console.log(`Loaded ${this.packedBoxes.length} boxes from database.`);
        this.renderBoxes(this.packedBoxes);
      },
      error: (err) => console.error('Failed to fetch existing plan', err)
    });
  }

  private drawTruckWireframe() {
    // 1. Draw the standard green truck bounding box
    const geometry = new THREE.BoxGeometry(this.TRUCK_W, this.TRUCK_H, this.TRUCK_L);
    geometry.translate(this.TRUCK_W / 2, this.TRUCK_H / 2, this.TRUCK_L / 2);
    
    const edges = new THREE.EdgesGeometry(geometry);
    const material = new THREE.LineBasicMaterial({ color: 0x00ff00, linewidth: 2, opacity: 0.3, transparent: true });
    const truckWireframe = new THREE.LineSegments(edges, material);
    
    this.truckGroup.add(truckWireframe);

    // 2. NEW: Draw the solid Floor (helps ground the 3D perspective)
    const floorGeometry = new THREE.PlaneGeometry(this.TRUCK_W, this.TRUCK_L);
    floorGeometry.rotateX(-Math.PI / 2); // Lay it flat
    floorGeometry.translate(this.TRUCK_W / 2, 0, this.TRUCK_L / 2);
    const floorMaterial = new THREE.MeshBasicMaterial({ color: 0x333333, side: THREE.DoubleSide, transparent: true, opacity: 0.5 });
    const floorMesh = new THREE.Mesh(floorGeometry, floorMaterial);
    
    this.truckGroup.add(floorMesh);

    // 3. NEW: Highlight the Loading Doors (Z = TRUCK_L)
    const doorGeometry = new THREE.PlaneGeometry(this.TRUCK_W, this.TRUCK_H);
    // Move it to the very back of the truck
    doorGeometry.translate(this.TRUCK_W / 2, this.TRUCK_H / 2, this.TRUCK_L);
    
    // Create a tinted orange pane for the door
    const doorMaterial = new THREE.MeshBasicMaterial({ 
      color: 0xff8800, 
      transparent: true, 
      opacity: 0.15, 
      side: THREE.DoubleSide 
    });
    const doorMesh = new THREE.Mesh(doorGeometry, doorMaterial);

    // Add a glowing orange border to the door frame
    const doorEdges = new THREE.EdgesGeometry(doorGeometry);
    const doorEdgeMaterial = new THREE.LineBasicMaterial({ color: 0xffaa00, linewidth: 3 });
    const doorWireframe = new THREE.LineSegments(doorEdges, doorEdgeMaterial);

    this.truckGroup.add(doorMesh);
    this.truckGroup.add(doorWireframe);
  }
}