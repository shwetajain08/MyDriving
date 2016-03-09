using System;
using System.Globalization;
using System.Collections.Generic;
using System.Threading.Tasks;

using CoreAnimation;
using CoreLocation;
using Foundation;
using MapKit;
using UIKit;

using MyTrips.ViewModel;

using Plugin.Permissions;
using Plugin.Permissions.Abstractions;

namespace MyTrips.iOS
{
	partial class CurrentTripViewController : UIViewController
	{
		List<CLLocationCoordinate2D> route;
		CarAnnotation currentLocationAnnotation;
		TripMapViewDelegate mapDelegate;

		CurrentTripViewModel CurrentTripViewModel { get; set; }

		public PastTripsDetailViewModel PastTripsDetailViewModel { get; set; }

		public CurrentTripViewController (IntPtr handle) : base (handle)
		{
		}

		public async override void ViewDidLoad()
		{
			base.ViewDidLoad();

			NavigationItem.RightBarButtonItem = null;

			if (PastTripsDetailViewModel == null)
			{
				await ConfigureCurrentTripUserInterface();
			}
			else
			{
				ConfigurePastTripUserInterface();
			}
		}

		public override void ViewDidAppear(bool animated)
		{
			base.ViewDidAppear(animated);

			if (recordButton.Hidden == true && PastTripsDetailViewModel == null)
			{
				recordButton.Pop(0.5, 0, 1);
			}
		}

		#region Current Trip User Interface Logic
		async Task ConfigureCurrentTripUserInterface()
		{
			// Configure map
			mapDelegate = new TripMapViewDelegate(UIColor.Red, 0.6);
			tripMapView.Delegate = mapDelegate;
			tripMapView.ShowsUserLocation = false;
			tripMapView.Camera.Altitude = 5000;

			// Setup record button
			recordButton.Hidden = true;
			recordButton.Layer.CornerRadius = recordButton.Frame.Width / 2;
			recordButton.Layer.MasksToBounds = true;
			recordButton.Layer.BorderColor = "5C5C5C".ToUIColor().CGColor;
			recordButton.Layer.BorderWidth = 1;
			recordButton.TouchUpInside += RecordButton_TouchUpInside;

			// Hide slider
			tripSlider.Hidden = true;
			wayPointA.Hidden = true;
			wayPointB.Hidden = true;

			UpdateRecordButton(false);
			tripInfoView.Alpha = 0;
			ResetTripInfoView();

			// Setup view model
			CurrentTripViewModel = new CurrentTripViewModel();
			CurrentTripViewModel.Geolocator.PositionChanged += Geolocator_PositionChanged;

			// Start tracking user location, pending permission from user.
			await CurrentTripViewModel.ExecuteStartTrackingTripCommandAsync().ContinueWith(async (task) =>
			{
				// If we don't have permission from the user, prompt a dialog requesting permission.
				await PromptPermissionsChangeDialog();
			});
		}

		void AnimateTripInfoView()
		{
			tripInfoView.FadeIn(0.3, 0);
		}

		void ResetMapViewState()
		{
			InvokeOnMainThread(() =>
		   {
				route = null;
				tripMapView.RemoveAnnotations(tripMapView.Annotations);

				if (tripMapView.Overlays != null)
				{
					tripMapView.RemoveOverlays(tripMapView.Overlays[0]);
				}
		   });
		}

		void ResetTripInfoView()
		{
			labelOneValue.Text = "N/A";
			labelTwoValue.Text = "0";
			labelThreeValue.Text = "0:00";
			labelFourValue.Text = "N/A";
		}

		void UpdateRecordButton(bool isRecording)
		{
			//Corner Radius
			var radiusAnimation = CABasicAnimation.FromKeyPath("cornerRadius");
			radiusAnimation.TimingFunction = CAMediaTimingFunction.FromName(CAMediaTimingFunction.EaseIn);
			radiusAnimation.From = NSNumber.FromNFloat(recordButton.Layer.CornerRadius);

			//Border Thickness
			var borderAnimation = CABasicAnimation.FromKeyPath("borderWidth");
			borderAnimation.TimingFunction = CAMediaTimingFunction.FromName(CAMediaTimingFunction.EaseIn);
			radiusAnimation.From = NSNumber.FromNFloat(recordButton.Layer.BorderWidth);

			//Animation Group
			var animationGroup = CAAnimationGroup.CreateAnimation();
			animationGroup.Animations = new CAAnimation[] { radiusAnimation, borderAnimation };
			animationGroup.Duration = 0.6;
			animationGroup.FillMode = CAFillMode.Forwards;

			recordButton.Layer.CornerRadius = isRecording ? 4 : recordButton.Frame.Width / 2;
			recordButton.Layer.BorderWidth = isRecording ? 2 : 3;

			recordButton.Layer.AddAnimation(animationGroup, "borderChanges");
		}

		async Task PromptPermissionsChangeDialog()
		{
			var status = await CrossPermissions.Current.CheckPermissionStatusAsync(Permission.Location);
			if (status == PermissionStatus.Denied)
			{
				InvokeOnMainThread(() =>
				{
					var alertController = UIAlertController.Create("Location Permission Denied", "Tracking your location is required to record trips. Visit the Settings app to change the permission status.", UIAlertControllerStyle.Alert);
					alertController.AddAction(UIAlertAction.Create("Change Permission", UIAlertActionStyle.Default, (obj) =>
					{
						var url = NSUrl.FromString(UIApplication.OpenSettingsUrlString);
						UIApplication.SharedApplication.OpenUrl(url);
					}));

					alertController.AddAction(UIAlertAction.Create("OK", UIAlertActionStyle.Cancel, null));

					PresentViewController(alertController, true, null);
				});
			}
		}

		void TakePhotoButton_Clicked(object sender, EventArgs e)
		{
			if (!CurrentTripViewModel.IsBusy && CurrentTripViewModel.IsRecording)
				CurrentTripViewModel?.TakePhotoCommand.Execute(null);
		}

		async void RecordButton_TouchUpInside(object sender, EventArgs e)
		{
			var position = await CurrentTripViewModel.Geolocator.GetPositionAsync();
			var coordinate = position.ToCoordinate();

			var endpoint = !CurrentTripViewModel.IsRecording ? "A" : "B";
			var annotation = new WaypointAnnotation(coordinate, endpoint);
			tripMapView.AddAnnotation(annotation);

			if (!CurrentTripViewModel.IsRecording)
			{
				if (NavigationItem.RightBarButtonItem == null)
					NavigationItem.SetRightBarButtonItem(takePhotoButton, true);

				NavigationItem.RightBarButtonItem.Clicked += TakePhotoButton_Clicked;

				UpdateRecordButton(true);
				ResetTripInfoView();
				AnimateTripInfoView();

				CurrentTripViewModel.StartRecordingTrip();
			}
			else
			{
				CurrentTripViewModel.StopRecordingTrip();
				ResetMapViewState();

				UpdateRecordButton(false);
				tripInfoView.Alpha = 0;

				NavigationItem.RightBarButtonItem.Clicked -= TakePhotoButton_Clicked;
				NavigationItem.SetRightBarButtonItem(null, true);

				var vc = Storyboard.InstantiateViewController("tripSummaryTableViewController") as TripSummaryTableViewController;
				vc.ViewModel = CurrentTripViewModel;
				PresentModalViewController(vc, true);
			}
		}

		void Geolocator_PositionChanged(object sender, Plugin.Geolocator.Abstractions.PositionEventArgs e)
		{
			var coordinate = e.Position.ToCoordinate();
			UpdateCarAnnotationPosition (coordinate);

			if (CurrentTripViewModel.IsRecording)
			{
				// Update trip information
				labelOneValue.Text = CurrentTripViewModel.FuelConsumption;
				labelOneTitle.Text = CurrentTripViewModel.FuelConsumptionUnits;
				labelThreeValue.Text = CurrentTripViewModel.ElapsedTime;
				labelTwoValue.Text = CurrentTripViewModel.CurrentTrip.Distance.ToString("F");
				labelTwoTitle.Text = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(CurrentTripViewModel.CurrentTrip.Units.ToLower());
				labelFourValue.Text = CurrentTripViewModel.Temperature;

				// If we already haven't starting tracking route yet, start that.
				if (route == null)
					StartTrackingRoute(coordinate);
				// Draw from last known coordinate to new coordinate.
				else
					DrawNewRouteWaypoint(coordinate);
			}
		}

		void StartTrackingRoute(CLLocationCoordinate2D coordinate)
		{
			route = new List<CLLocationCoordinate2D>();

			var count = CurrentTripViewModel.CurrentTrip.Points.Count;
			if (count == 0)
			{
				route.Add(coordinate);
			}
			else
			{
				var firstPoint = CurrentTripViewModel.CurrentTrip.Points?[0];
				var firstCoordinate = new CLLocationCoordinate2D(firstPoint.Latitude, firstPoint.Longitude);
				route.Add(firstCoordinate);
			}
		}
		#endregion

		#region Past Trip User Interface Logic
		void ConfigurePastTripUserInterface()
		{
			NavigationItem.Title = PastTripsDetailViewModel.Title;

			var coordinateCount = PastTripsDetailViewModel.Trip.Points.Count;

			// Setup map
			mapDelegate = new TripMapViewDelegate(UIColor.Blue, 0.6);
			tripMapView.Delegate = mapDelegate;
			tripMapView.ShowsUserLocation = false;
			tripMapView.Camera.Altitude = 5000;
			tripMapView.SetVisibleMapRect(MKPolyline.FromCoordinates(PastTripsDetailViewModel.Trip.Points.ToCoordinateArray()).BoundingMapRect, new UIEdgeInsets(25, 25, 25, 25), false);

			// Draw endpoints
			var startEndpoint = new WaypointAnnotation(PastTripsDetailViewModel.Trip.Points[0].ToCoordinate(), "A");
			tripMapView.AddAnnotation(startEndpoint);

			var endEndpoint = new WaypointAnnotation(PastTripsDetailViewModel.Trip.Points[coordinateCount - 1].ToCoordinate(), "B");
			tripMapView.AddAnnotation(endEndpoint);

			// Draw route
			tripMapView.DrawRoute(PastTripsDetailViewModel.Trip.Points.ToCoordinateArray());

			// Draw car
			var carCoordinate = PastTripsDetailViewModel.Trip.Points[coordinateCount / 2].ToCoordinate();
			currentLocationAnnotation = new CarAnnotation(carCoordinate, UIColor.Blue);
			tripMapView.AddAnnotation(currentLocationAnnotation);

			ConfigureSlider();
			ConfigureWayPointButtons();

			// Hide record button
			recordButton.Hidden = true;

			// Show slider 
			sliderView.Hidden = false;

			startTimeLabel.Hidden = false;
			endTimeLabel.Hidden = false;
			startTimeLabel.Text = PastTripsDetailViewModel.Trip.StartTimeDisplay;
			endTimeLabel.Text = PastTripsDetailViewModel.Trip.EndTimeDisplay;

			labelOneTitle.Text = "Avg Speed";
			labelOneValue.Text = PastTripsDetailViewModel.Trip.AverageSpeed.ToString();
			labelTwoTitle.Text = PastTripsDetailViewModel.Settings.MetricDistance ? "Kilometers" : "Miles";
			labelTwoValue.Text = PastTripsDetailViewModel.Trip.TotalDistanceNoUnits;
			labelThreeTitle.Text = "Consumption";
			labelThreeValue.Text = PastTripsDetailViewModel.Trip.FuelUsed.ToString();
			labelFourTitle.Text = "Emissions";
			labelFourValue.Text = PastTripsDetailViewModel.Trip.Emissions.ToString();
		}

		void ConfigureSlider()
		{
			var dataPoints = PastTripsDetailViewModel.Trip.Points.Count - 1;
			tripSlider.MinValue = 0;
			tripSlider.MaxValue = dataPoints;
			tripSlider.Value = PastTripsDetailViewModel.Trip.Points.Count / 2;

			tripSlider.ValueChanged += TripSlider_ValueChanged;
		}

		void ConfigureWayPointButtons()
		{
			wayPointA.Layer.CornerRadius = wayPointA.Frame.Width / 2;
			wayPointA.Layer.BorderWidth = 2;
			wayPointA.Layer.BorderColor = UIColor.White.CGColor;
			wayPointA.TouchUpInside += delegate 
			{
				tripSlider.Value = 0;
				TripSlider_ValueChanged(this, null);

			};

			wayPointB.Layer.CornerRadius = wayPointB.Frame.Width / 2;
			wayPointB.Layer.BorderWidth = 2;
			wayPointB.Layer.BorderColor = UIColor.White.CGColor;
			wayPointB.TouchUpInside += delegate 
			{
				tripSlider.Value = tripSlider.MaxValue;
				TripSlider_ValueChanged(this, null);
			};
		}

		void TripSlider_ValueChanged(object sender, EventArgs e)
		{
			// Move car to coordinate
			var value = (int)tripSlider.Value;
			var coordinate = PastTripsDetailViewModel.Trip.Points[value].ToCoordinate();
			UpdateCarAnnotationPosition(coordinate);
		}
		#endregion

		#region Shared User Interface Logic
		void UpdateCarAnnotationPosition(CLLocationCoordinate2D coordinate)
		{
			if (currentLocationAnnotation != null)
			{
				tripMapView.RemoveAnnotation(currentLocationAnnotation);
			}

			var color = CurrentTripViewModel != null && CurrentTripViewModel.IsRecording ? UIColor.Red : UIColor.Blue;
			currentLocationAnnotation = new CarAnnotation(coordinate, color);

			tripMapView.AddAnnotation(currentLocationAnnotation);
			tripMapView.Camera.CenterCoordinate = coordinate;
		}

		void DrawNewRouteWaypoint(CLLocationCoordinate2D coordinate)
		{
			route.Add(coordinate);

			if (tripMapView.Overlays != null)
				tripMapView.RemoveOverlays(tripMapView.Overlays);

			tripMapView.DrawRoute(route.ToArray());
		}
		#endregion
	}
}